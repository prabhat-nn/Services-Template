#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Nexenova.Services.Core;

namespace Nexenova.Services.RemoteConfig
{
    /// <summary>
    /// Remote-tunable configuration. Getters never fail: before fetch (or on fetch
    /// failure) they return the provided default; fetched numeric values pass through
    /// declarative clamp validation. Fetch failure leaves the module in degraded mode
    /// (defaults in force) without failing the boot.
    /// </summary>
    internal sealed class RemoteConfigServiceModule : IRemoteConfigService, IServiceModule
    {
        private const string Tag = "RemoteConfig";

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
        };

        private readonly IRemoteConfigSdk _sdk;
        private readonly ConfigValidator _validator;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly RetryPolicy _retry;
        private readonly RemoteConfigOptions _options;

        public string ModuleName => "RemoteConfig";
        public InitializationStage Stage => InitializationStage.Data;
        public bool IsRequired => false;

        public bool IsFetched { get; private set; }

        public RemoteConfigServiceModule(
            IRemoteConfigSdk sdk,
            ConfigValidator validator,
            IEventBus events,
            IServiceLogger logger,
            RetryPolicy retry,
            RemoteConfigOptions options)
        {
            _sdk = sdk;
            _validator = validator;
            _events = events;
            _logger = logger;
            _retry = retry;
            _options = options;
        }

        public async UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            var fetch = await FetchAsync(ct);
            if (fetch.IsFailure)
            {
                // Stay degraded with baked-in defaults rather than failing the boot.
                _logger.Warning(Tag, $"Initial fetch failed ({fetch.Error.Code}) — serving defaults until the next fetch.");
            }
            return ServiceResult.Ok();
        }

        public async UniTask<ServiceResult<Unit>> FetchAsync(CancellationToken ct = default)
        {
            var result = await _retry.ExecuteAsync<Unit>("RemoteConfig.Fetch", async token =>
            {
                try
                {
                    await _sdk.FetchAsync(_options.AppVersion, token);
                    return ServiceResult.Ok();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = RemoteConfigErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Fetch failed: {error}", ex);
                    return ServiceResult.Fail(error);
                }
            }, ct);

            if (result.IsSuccess)
            {
                IsFetched = true;
                _events.Publish(new RemoteConfigFetchedEvent());
            }

            return result;
        }

        public int GetInt(string key, int defaultValue) =>
            !IsFetched ? defaultValue : (int)_validator.Clamp(key, (long)_sdk.GetInt(key, defaultValue));

        public long GetLong(string key, long defaultValue) =>
            !IsFetched ? defaultValue : _validator.Clamp(key, _sdk.GetLong(key, defaultValue));

        public float GetFloat(string key, float defaultValue) =>
            !IsFetched ? defaultValue : (float)_validator.Clamp(key, (double)_sdk.GetFloat(key, defaultValue));

        public bool GetBool(string key, bool defaultValue) =>
            !IsFetched ? defaultValue : _sdk.GetBool(key, defaultValue);

        public string GetString(string key, string defaultValue) =>
            !IsFetched ? defaultValue : _sdk.GetString(key, defaultValue);

        public ServiceResult<T> GetJson<T>(string key)
        {
            if (!IsFetched || !_sdk.HasKey(key))
                return ServiceResult<T>.Failure(new ServiceError(ServiceErrorCode.NotFound, $"No fetched config under '{key}'."));

            var json = _sdk.GetJson(key, "null");
            try
            {
                var value = JsonConvert.DeserializeObject<T>(json, JsonSettings);
                return value == null
                    ? ServiceResult<T>.Failure(ServiceError.Validation($"Config '{key}' deserialized to null."))
                    : ServiceResult<T>.Success(value);
            }
            catch (Exception ex)
            {
                _logger.Warning(Tag, $"Config '{key}' is not valid {typeof(T).Name} JSON — falling back to default. ({ex.Message})");
                return ServiceResult<T>.Failure(ServiceErrorCode.Validation, $"Config '{key}' does not match {typeof(T).Name}.", ex);
            }
        }
    }
}
