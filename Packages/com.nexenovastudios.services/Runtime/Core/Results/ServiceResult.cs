#nullable enable
using System;

namespace Nexenova.Services
{
    /// <summary>Marker value for results that carry no payload.</summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        public static readonly Unit Value = default;
        public bool Equals(Unit other) => true;
        public override bool Equals(object? obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public override string ToString() => "()";
    }

    /// <summary>
    /// Result wrapper for every service operation that can fail at runtime.
    /// Public service APIs never throw for expected failures — they return this.
    /// </summary>
    public readonly struct ServiceResult<T>
    {
        private readonly T _value;
        private readonly ServiceError? _error;

        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;

        public T Value =>
            IsSuccess ? _value : throw new InvalidOperationException($"Cannot access Value of a failed result: {_error}");

        public ServiceError Error =>
            IsSuccess ? throw new InvalidOperationException("Cannot access Error of a successful result.") : _error!;

        private ServiceResult(bool isSuccess, T value, ServiceError? error)
        {
            IsSuccess = isSuccess;
            _value = value;
            _error = error;
        }

        public static ServiceResult<T> Success(T value) => new(true, value, null);

        public static ServiceResult<T> Failure(ServiceError error) =>
            new(false, default!, error ?? throw new ArgumentNullException(nameof(error)));

        public static ServiceResult<T> Failure(ServiceErrorCode code, string message, Exception? cause = null) =>
            Failure(new ServiceError(code, message, cause));

        public ServiceResult<TOut> Map<TOut>(Func<T, TOut> map) =>
            IsSuccess ? ServiceResult<TOut>.Success(map(Value)) : ServiceResult<TOut>.Failure(Error);

        public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<ServiceError, TOut> onFailure) =>
            IsSuccess ? onSuccess(Value) : onFailure(Error);

        public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
    }

    /// <summary>Convenience factories for <see cref="ServiceResult{T}"/> of <see cref="Unit"/>.</summary>
    public static class ServiceResult
    {
        public static ServiceResult<Unit> Ok() => ServiceResult<Unit>.Success(Unit.Value);

        public static ServiceResult<Unit> Fail(ServiceError error) => ServiceResult<Unit>.Failure(error);

        public static ServiceResult<Unit> Fail(ServiceErrorCode code, string message, Exception? cause = null) =>
            ServiceResult<Unit>.Failure(code, message, cause);
    }
}
