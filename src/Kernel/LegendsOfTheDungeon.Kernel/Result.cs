namespace LegendsOfTheDungeon.Kernel;

/// <summary>
/// 表示不携带成功值的操作结果。default 值未初始化，不能被解释为业务失败。
/// </summary>
public readonly record struct Result
{
    private const byte SuccessState = 1;
    private const byte FailureState = 2;

    private readonly byte _state;
    private readonly Error? _error;

    private Result(byte state, Error? error)
    {
        _state = state;
        _error = error;
    }

    /// <summary>
    /// 获取结果是否成功；default/未初始化结果会被拒绝。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化时抛出。</exception>
    public bool IsSuccess => _state switch
    {
        SuccessState => true,
        FailureState => false,
        _ => throw new InvalidOperationException("Result 未初始化。")
    };

    /// <summary>
    /// 获取结果是否失败；default/未初始化结果会被拒绝。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化时抛出。</exception>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// 获取失败错误。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化或当前为成功结果时抛出。</exception>
    public Error Error
    {
        get
        {
            if (_state == 0)
            {
                throw new InvalidOperationException("Result 未初始化。");
            }

            if (_state != FailureState)
            {
                throw new InvalidOperationException("成功 Result 没有 Error。");
            }

            return _error!;
        }
    }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <returns>已初始化的成功结果。</returns>
    public static Result Success()
    {
        return new Result(SuccessState, null);
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="error">稳定错误对象。</param>
    /// <returns>已初始化的失败结果。</returns>
    /// <exception cref="ArgumentNullException">错误对象为 null 时抛出。</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(FailureState, error);
    }
}

/// <summary>
/// 表示携带成功值的操作结果。default 值未初始化，不能被解释为业务失败。
/// </summary>
/// <typeparam name="T">成功值类型。</typeparam>
public readonly record struct Result<T>
{
    private const byte SuccessState = 1;
    private const byte FailureState = 2;

    private readonly byte _state;
    private readonly T? _value;
    private readonly Error? _error;

    private Result(byte state, T? value, Error? error)
    {
        _state = state;
        _value = value;
        _error = error;
    }

    /// <summary>
    /// 获取结果是否成功；default/未初始化结果会被拒绝。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化时抛出。</exception>
    public bool IsSuccess => _state switch
    {
        SuccessState => true,
        FailureState => false,
        _ => throw new InvalidOperationException("Result 未初始化。")
    };

    /// <summary>
    /// 获取结果是否失败；default/未初始化结果会被拒绝。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化时抛出。</exception>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// 获取成功值。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化或当前为失败结果时抛出。</exception>
    public T Value
    {
        get
        {
            if (_state == 0)
            {
                throw new InvalidOperationException("Result 未初始化。");
            }

            if (_state != SuccessState)
            {
                throw new InvalidOperationException("失败 Result 没有成功值。");
            }

            return _value!;
        }
    }

    /// <summary>
    /// 获取失败错误。
    /// </summary>
    /// <exception cref="InvalidOperationException">结果未初始化或当前为成功结果时抛出。</exception>
    public Error Error
    {
        get
        {
            if (_state == 0)
            {
                throw new InvalidOperationException("Result 未初始化。");
            }

            if (_state != FailureState)
            {
                throw new InvalidOperationException("成功 Result 没有 Error。");
            }

            return _error!;
        }
    }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="value">非 null 成功值。</param>
    /// <returns>已初始化的成功结果。</returns>
    /// <exception cref="ArgumentNullException">成功值为 null 时抛出。</exception>
    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(SuccessState, value, null);
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="error">稳定错误对象。</param>
    /// <returns>已初始化的失败结果。</returns>
    /// <exception cref="ArgumentNullException">错误对象为 null 时抛出。</exception>
    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(FailureState, default, error);
    }
}
