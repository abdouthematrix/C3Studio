namespace C3Studio.Core.Models;

public enum ValidationStatus { Pending, Ok, Fail }

public class ValidationItem
{
    public string           Category { get; set; } = string.Empty;
    public string           Name     { get; set; } = string.Empty;
    public ValidationStatus Status   { get; set; } = ValidationStatus.Pending;
}
