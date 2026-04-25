using NetworkBlaster.Soap;
using Xunit;

namespace NetworkBlaster.Tests.Soap;

public class SoapFaultTests
{
    [Fact]
    public void Construction_PreservesAllFields()
    {
        var fault = new SoapFault(SoapVersion.V12, "env:Sender", "Bad input", "<Detail/>");
        Assert.Equal(SoapVersion.V12, fault.Version);
        Assert.Equal("env:Sender", fault.Code);
        Assert.Equal("Bad input", fault.Reason);
        Assert.Equal("<Detail/>", fault.Detail);
    }

    [Fact]
    public void Message_IncludesVersion_Code_Reason()
    {
        var fault = new SoapFault(SoapVersion.V11, "soap:Server", "Boom");
        Assert.Contains("1.1", fault.Message);
        Assert.Contains("soap:Server", fault.Message);
        Assert.Contains("Boom", fault.Message);
    }

    [Fact]
    public void Detail_OptionalDefaultsNull()
    {
        var fault = new SoapFault(SoapVersion.V11, "soap:Server", "Boom");
        Assert.Null(fault.Detail);
    }
}
