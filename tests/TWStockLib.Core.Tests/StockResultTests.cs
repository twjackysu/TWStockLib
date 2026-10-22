namespace TWStockLib.Core.Tests;

public class StockResultTests
{
    [Fact]
    public void Ok_CarriesValue_AndIsSuccess()
    {
        var result = StockResult<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Ok_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => StockResult<string>.Ok(null!));
    }

    [Fact]
    public void Fail_CarriesCodeMessageAndException()
    {
        var cause = new HttpRequestException("boom");

        var result = StockResult<int>.Fail("CODE", "message", cause);

        Assert.False(result.IsSuccess);
        Assert.Equal("CODE", result.ErrorCode);
        Assert.Equal("message", result.ErrorMessage);
        Assert.Same(cause, result.Exception);
    }

    [Fact]
    public void TryGetValue_ReturnsTheValueOnlyOnSuccess()
    {
        Assert.True(StockResult<string>.Ok("2330").TryGetValue(out var value));
        Assert.Equal("2330", value);

        Assert.False(StockResult<string>.Fail("CODE", "message").TryGetValue(out _));
    }

    [Fact]
    public void GetValueOrThrow_OnFailure_ThrowsWithTheErrorCodeAndCause()
    {
        var cause = new HttpRequestException("boom");
        var result = StockResult<string>.Fail(StockErrorCodes.UpstreamError, "上游錯誤", cause);

        var error = Assert.Throws<StockQueryException>(() => result.GetValueOrThrow());

        Assert.Equal(StockErrorCodes.UpstreamError, error.ErrorCode);
        Assert.Equal("上游錯誤", error.Message);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void GetValueOrThrow_OnSuccess_ReturnsTheValue()
    {
        Assert.Equal("2330", StockResult<string>.Ok("2330").GetValueOrThrow());
    }

    [Fact]
    public void ToString_DescribesTheOutcome()
    {
        Assert.Equal("Ok(42)", StockResult<int>.Ok(42).ToString());
        Assert.Equal("Fail(NOT_FOUND: 查無資料)", StockResult<int>.Fail("NOT_FOUND", "查無資料").ToString());
    }
}
