namespace Sanad.BuildingBlocks.Application.Results;

public class Result<T> : Result
{
    private readonly T? _value;

    protected Result(T value)
        : base(true, Error.None)
    {
        _value = value;
    }

    protected Result(Error error)
        : base(false, error)
    {
        
    }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException();

    public static Result<T> Success(T value)
        => new(value);

    public static new Result<T> Failure(Error error) => 
        new(error);
}