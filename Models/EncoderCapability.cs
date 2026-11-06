namespace MediaStudio.Models;

public enum EncoderAvailability { Unknown, Available, Unavailable }

public sealed record EncoderCapability(EncoderAvailability Availability, string Message);
