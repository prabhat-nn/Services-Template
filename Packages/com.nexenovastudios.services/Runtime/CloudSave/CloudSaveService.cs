#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Nexenova.Services.Core;

namespace Nexenova.Services.CloudSave
{
    /// <summary>
    /// Versioned, conflict-aware persistence over UGS Cloud Save with an offline
    /// write-through cache. Conflict resolution is last-write-wins after a forced
    /// reload: the service reloads and publishes the conflict; the game decides
    /// whether to re-save. Offline writes are accepted locally and replayed on
    /// the next boot.
    /// </summary>
    internal sealed class CloudSaveService : ICloudSaveService, IServiceModule
    {
        private const string Tag = "CloudSave";

        private readonly ICloudSaveSdk _sdk;
        private readonly ILocalSaveCache _cache;
        private readonly ISaveMigrator _migrator;
        private readonly IEventBus _events;
        private readonly IServiceLogger _logger;
        private readonly RetryPolicy _retry;
        private readonly CloudSaveOptions _options;

        private readonly Dictionary<string, string?> _writeLocks = new();
        private readonly HashSet<string> _savesInFlight = new();
        private bool _initialized;

        public string ModuleName => "CloudSave";
        public InitializationStage Stage => InitializationStage.Data;
        public bool IsRequired => false;

        public CloudSaveService(
            ICloudSaveSdk sdk,
            ILocalSaveCache cache,
            ISaveMigrator migrator,
            IEventBus events,
            IServiceLogger logger,
            RetryPolicy retry,
            CloudSaveOptions options)
        {
            _sdk = sdk;
            _cache = cache;
            _migrator = migrator;
            _events = events;
            _logger = logger;
            _retry = retry;
            _options = options;
        }

        public async UniTask<ServiceResult<Unit>> InitializeAsync(CancellationToken ct)
        {
            _initialized = true;
            await ReplayPendingWritesAsync(ct);
            return ServiceResult.Ok();
        }

        public async UniTask<ServiceResult<T>> LoadAsync<T>(string key, CancellationToken ct = default)
        {
            var guard = ValidateKey(key);
            if (guard != null)
                return ServiceResult<T>.Failure(guard);

            var fullKey = FullKey(key);
            var remote = await _retry.ExecuteAsync<(bool found, string json, string? writeLock)>(
                $"CloudSave.Load({key})",
                async token =>
                {
                    try
                    {
                        return ServiceResult<(bool, string, string?)>.Success(await _sdk.LoadAsync(fullKey, token));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        var error = CloudSaveErrorMapper.Map(ex);
                        _logger.Error(Tag, $"Load '{key}' failed: {error}", ex);
                        return ServiceResult<(bool, string, string?)>.Failure(error);
                    }
                }, ct);

            if (remote.IsSuccess)
            {
                var (found, envelopeJson, writeLock) = remote.Value;
                if (!found)
                {
                    if (_cache.PendingKeys.Contains(key) && _cache.TryRead(key, out var local))
                        return DeserializeEnvelope<T>(key, local);
                    return ServiceResult<T>.Failure(new ServiceError(ServiceErrorCode.NotFound, $"No cloud save under key '{key}'."));
                }

                _writeLocks[key] = writeLock;
                _cache.Write(key, envelopeJson);
                return DeserializeEnvelope<T>(key, envelopeJson);
            }

            if (remote.Error.IsRetryable && _cache.TryRead(key, out var cached))
            {
                _logger.Warning(Tag, $"Load '{key}' served from local cache (offline).");
                return DeserializeEnvelope<T>(key, cached);
            }

            return ServiceResult<T>.Failure(remote.Error);
        }

        public async UniTask<ServiceResult<Unit>> SaveAsync<T>(string key, T value, CancellationToken ct = default)
        {
            var guard = ValidateKey(key);
            if (guard != null)
                return ServiceResult.Fail(guard);

            string payloadJson;
            try
            {
                payloadJson = JsonConvert.SerializeObject(value, SaveJson.Settings);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, $"Value for '{key}' is not serializable: {ex.Message}", ex);
            }

            if (Encoding.UTF8.GetByteCount(payloadJson) > _options.MaxPayloadBytes)
                return ServiceResult.Fail(ServiceError.Validation(
                    $"Payload for '{key}' exceeds {_options.MaxPayloadBytes / 1024} KB. Split the data or raise the limit in ServicesSettings."));

            if (!_savesInFlight.Add(key))
                return ServiceResult.Fail(ServiceError.AlreadyInProgress($"Save('{key}')"));

            try
            {
                var envelope = new SaveEnvelope
                {
                    SchemaVersion = _options.SchemaVersion,
                    SavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    PayloadJson = payloadJson,
                };
                var envelopeJson = JsonConvert.SerializeObject(envelope, SaveJson.Settings);

                _cache.Write(key, envelopeJson);
                _cache.MarkPending(key);

                return await UploadAsync(key, envelopeJson, ct);
            }
            finally
            {
                _savesInFlight.Remove(key);
            }
        }

        public async UniTask<ServiceResult<Unit>> DeleteAsync(string key, CancellationToken ct = default)
        {
            var guard = ValidateKey(key);
            if (guard != null)
                return ServiceResult.Fail(guard);

            var fullKey = FullKey(key);
            var result = await _retry.ExecuteAsync<Unit>($"CloudSave.Delete({key})", async token =>
            {
                try
                {
                    _writeLocks.TryGetValue(key, out var writeLock);
                    await _sdk.DeleteAsync(fullKey, writeLock, token);
                    return ServiceResult.Ok();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = CloudSaveErrorMapper.Map(ex);
                    if (error.Code == ServiceErrorCode.NotFound)
                        return ServiceResult.Ok();
                    _logger.Error(Tag, $"Delete '{key}' failed: {error}", ex);
                    return ServiceResult.Fail(error);
                }
            }, ct);

            if (result.IsSuccess)
            {
                _writeLocks.Remove(key);
                _cache.Delete(key);
            }
            else if (result.Error.Code == ServiceErrorCode.Conflict)
            {
                await HandleConflictAsync(key, ct);
            }

            return result;
        }

        public async UniTask<ServiceResult<IReadOnlyList<string>>> ListKeysAsync(CancellationToken ct = default)
        {
            var prefix = _options.KeyPrefix + ".";
            var result = await _retry.ExecuteAsync<IReadOnlyList<string>>("CloudSave.ListKeys", async token =>
            {
                try
                {
                    var keys = await _sdk.ListKeysAsync(token);
                    return ServiceResult<IReadOnlyList<string>>.Success(
                        keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                            .Select(k => k.Substring(prefix.Length))
                            .ToList());
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = CloudSaveErrorMapper.Map(ex);
                    _logger.Error(Tag, $"ListKeys failed: {error}", ex);
                    return ServiceResult<IReadOnlyList<string>>.Failure(error);
                }
            }, ct);

            if (result.IsFailure && result.Error.IsRetryable)
            {
                _logger.Warning(Tag, "ListKeys served from local cache (offline).");
                return ServiceResult<IReadOnlyList<string>>.Success(_cache.Keys);
            }

            return result;
        }


        private async UniTask<ServiceResult<Unit>> UploadAsync(string key, string envelopeJson, CancellationToken ct)
        {
            var fullKey = FullKey(key);
            var result = await _retry.ExecuteAsync<Unit>($"CloudSave.Save({key})", async token =>
            {
                try
                {
                    _writeLocks.TryGetValue(key, out var writeLock);
                    var newLock = await _sdk.SaveAsync(fullKey, envelopeJson, writeLock, token);
                    _writeLocks[key] = newLock;
                    return ServiceResult.Ok();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var error = CloudSaveErrorMapper.Map(ex);
                    _logger.Error(Tag, $"Save '{key}' failed: {error}", ex);
                    return ServiceResult.Fail(error);
                }
            }, ct);

            if (result.IsSuccess)
            {
                _cache.ClearPending(key);
                _events.Publish(new CloudDataSavedEvent(key));
                return result;
            }

            if (result.Error.Code == ServiceErrorCode.Conflict)
            {
                _cache.ClearPending(key);
                await HandleConflictAsync(key, ct);
                return result;
            }

            if (result.Error.IsRetryable)
            {
                _logger.Warning(Tag, $"Save '{key}' accepted offline — queued for replay.");
                return ServiceResult.Ok();
            }

            _cache.ClearPending(key);
            return result;
        }

        private async UniTask HandleConflictAsync(string key, CancellationToken ct)
        {
            try
            {
                var (found, envelopeJson, writeLock) = await _sdk.LoadAsync(FullKey(key), ct);
                if (found)
                {
                    _writeLocks[key] = writeLock;
                    _cache.Write(key, envelopeJson);
                }
                else
                {
                    _writeLocks.Remove(key);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning(Tag, $"Post-conflict reload for '{key}' failed: {ex.Message}");
            }

            _events.Publish(new CloudDataConflictDetectedEvent(key));
        }

        private async UniTask ReplayPendingWritesAsync(CancellationToken ct)
        {
            foreach (var key in _cache.PendingKeys.ToList())
            {
                if (!_cache.TryRead(key, out var envelopeJson))
                {
                    _cache.ClearPending(key);
                    continue;
                }

                _logger.Info(Tag, $"Replaying queued offline write for '{key}'.");
                await UploadAsync(key, envelopeJson, ct);
            }
        }

        private ServiceResult<T> DeserializeEnvelope<T>(string key, string envelopeJson)
        {
            SaveEnvelope? envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<SaveEnvelope>(envelopeJson, SaveJson.Settings);
            }
            catch (Exception ex)
            {
                return ServiceResult<T>.Failure(ServiceErrorCode.Validation, $"Save data under '{key}' is not a valid envelope.", ex);
            }

            if (envelope == null || envelope.SchemaVersion <= 0)
                return ServiceResult<T>.Failure(ServiceError.Validation($"Save data under '{key}' is missing the envelope."));

            if (envelope.SchemaVersion > _options.SchemaVersion)
                return ServiceResult<T>.Failure(ServiceError.Validation(
                    $"Save '{key}' was written by a newer app (schema {envelope.SchemaVersion} > {_options.SchemaVersion})."));

            var payloadJson = envelope.SchemaVersion < _options.SchemaVersion
                ? _migrator.Migrate(key, envelope.SchemaVersion, _options.SchemaVersion, envelope.PayloadJson)
                : envelope.PayloadJson;

            try
            {
                var value = JsonConvert.DeserializeObject<T>(payloadJson, SaveJson.Settings);
                return value == null
                    ? ServiceResult<T>.Failure(ServiceError.Validation($"Save '{key}' deserialized to null."))
                    : ServiceResult<T>.Success(value);
            }
            catch (Exception ex)
            {
                return ServiceResult<T>.Failure(ServiceErrorCode.Validation, $"Save '{key}' payload does not match {typeof(T).Name}.", ex);
            }
        }

        private ServiceError? ValidateKey(string key)
        {
            if (!_initialized)
                return ServiceError.NotInitialized(ModuleName);
            if (string.IsNullOrWhiteSpace(key))
                return ServiceError.Validation("Key must not be empty.");
            if (key.Contains('.') || key.Any(char.IsWhiteSpace))
                return ServiceError.Validation($"Key '{key}' must not contain '.' or whitespace.");
            return null;
        }

        private string FullKey(string key) => $"{_options.KeyPrefix}.{key}";
    }
}
