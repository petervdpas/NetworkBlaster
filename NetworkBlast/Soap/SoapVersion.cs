namespace NetworkBlast.Soap;

/// <summary>
/// SOAP protocol version, governing envelope namespace, content type, and the
/// transport of the action identifier.
/// </summary>
public enum SoapVersion
{
    /// <summary>
    /// SOAP 1.1 — namespace <c>http://schemas.xmlsoap.org/soap/envelope/</c>,
    /// content type <c>text/xml</c>, action travels in the <c>SOAPAction</c> header.
    /// </summary>
    V11,

    /// <summary>
    /// SOAP 1.2 — namespace <c>http://www.w3.org/2003/05/soap-envelope</c>,
    /// content type <c>application/soap+xml</c>, action travels in the
    /// <c>action</c> parameter of the content type.
    /// </summary>
    V12,
}
