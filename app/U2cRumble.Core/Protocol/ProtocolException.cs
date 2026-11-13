namespace U2cRumble.Core.Protocol;

/// <summary>The controller didn't answer, or answered something that doesn't belong to the request.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message)
    {
    }
}
