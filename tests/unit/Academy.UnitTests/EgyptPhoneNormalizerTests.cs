using Academy.Api.Auth;

namespace Academy.UnitTests;

public sealed class EgyptPhoneNormalizerTests
{
    [Theory]
    [InlineData("01012345678")]
    [InlineData(" 010 1234 5678 ")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨")]
    [InlineData("۰۱۰۱۲۳۴۵۶۷۸")]
    [InlineData("+201012345678")]
    [InlineData("00201012345678")]
    [InlineData("+20 (0) 10-1234-5678")]
    public void Equivalent_local_and_international_numbers_use_one_identity(string phone) =>
        Assert.Equal("+201012345678", EgyptPhoneNormalizer.NormalizeForNewMobile(phone));

    [Theory]
    [InlineData("1012345678")]
    [InlineData("0101234567")]
    [InlineData("010123456789")]
    [InlineData("0101234+5678")]
    [InlineData("+9711012345678")]
    [InlineData("02212345678")]
    [InlineData("01312345678")]
    [InlineData("010abc45678")]
    public void Invalid_new_mobile_numbers_are_rejected(string phone) =>
        Assert.Null(EgyptPhoneNormalizer.NormalizeForNewMobile(phone));
}
