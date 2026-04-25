using System;

namespace NetworkBlaster.Soap;

/// <summary>
/// Thrown when a SOAP response carries a <c>&lt;Fault&gt;</c> element. Captures the
/// version-specific code, reason, and optional detail payload so callers can
/// branch on the failure shape without re-parsing XML.
/// </summary>
public sealed class SoapFault : Exception
{
    /// <summary>The SOAP version of the envelope that carried the fault.</summary>
    public SoapVersion Version { get; }

    /// <summary>
    /// Fault code: in 1.1 the bare <c>faultcode</c> string (e.g. <c>soap:Server</c>);
    /// in 1.2 the <c>Code/Value</c> text (e.g. <c>soap:Sender</c>).
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Human-readable reason: in 1.1 the <c>faultstring</c>; in 1.2 the
    /// first <c>Reason/Text</c> child.
    /// </summary>
    public string Reason { get; }

    /// <summary>Optional <c>Detail</c> payload, returned as XML text.</summary>
    public string? Detail { get; }

    /// <summary>Builds a SOAP fault with the parsed pieces.</summary>
    public SoapFault(SoapVersion version, string code, string reason, string? detail = null)
        : base($"SOAP {(version == SoapVersion.V11 ? "1.1" : "1.2")} Fault [{code}]: {reason}")
    {
        Version = version;
        Code = code;
        Reason = reason;
        Detail = detail;
    }
}
