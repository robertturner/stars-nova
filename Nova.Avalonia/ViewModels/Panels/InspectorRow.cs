namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One label/value line in the Inspector panel.
/// </summary>
public class InspectorRow
{
    public string Label { get; }

    public string Value { get; }

    public InspectorRow(string label, string value)
    {
        Label = label;
        Value = value;
    }
}
