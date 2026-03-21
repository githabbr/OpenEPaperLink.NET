namespace OEPLLib;

public sealed record JsonRotateCommand(int Rotation) : IOeplJsonCommand
{
    public object ToPayload() => new Dictionary<string, object> { ["rotate"] = Rotation };
}

public sealed record JsonTextCommand(
    int X,
    int Y,
    string Text,
    string FontName,
    OeplJsonColor Color,
    OeplJsonTextAlignment? Alignment = null,
    int? Size = null,
    OeplJsonColor? BackgroundColor = null) : IOeplJsonCommand
{
    public object ToPayload()
    {
        var values = new List<object> { X, Y, Text, FontName, Color.Value };
        if (Alignment is not null) values.Add((int)Alignment.Value);
        if (Size is not null)
        {
            if (Alignment is null) values.Add((int)OeplJsonTextAlignment.Left);
            values.Add(Size.Value);
        }

        if (BackgroundColor is not null)
        {
            if (Alignment is null) values.Add((int)OeplJsonTextAlignment.Left);
            if (Size is null) values.Add(0);
            values.Add(BackgroundColor.Value.Value);
        }

        return new Dictionary<string, object> { ["text"] = values };
    }
}

public sealed record JsonTextBoxCommand(
    int X,
    int Y,
    int Width,
    int Height,
    string Text,
    string FontName,
    OeplJsonColor? Color = null,
    float? LineHeight = null,
    OeplJsonTextAlignment? Alignment = null) : IOeplJsonCommand
{
    public object ToPayload()
    {
        var values = new List<object> { X, Y, Width, Height, Text, FontName };
        if (Color is not null) values.Add(Color.Value.Value);
        if (LineHeight is not null)
        {
            if (Color is null) values.Add(OeplJsonColor.Black.Value);
            values.Add(LineHeight.Value);
        }

        if (Alignment is not null)
        {
            if (Color is null) values.Add(OeplJsonColor.Black.Value);
            if (LineHeight is null) values.Add(1);
            values.Add((int)Alignment.Value);
        }

        return new Dictionary<string, object> { ["textbox"] = values };
    }
}

public sealed record JsonBoxCommand(
    int X,
    int Y,
    int Width,
    int Height,
    OeplJsonColor Fill,
    OeplJsonColor? BorderColor = null,
    int? BorderWidth = null) : IOeplJsonCommand
{
    public object ToPayload()
    {
        var values = new List<object> { X, Y, Width, Height, Fill.Value };
        if (BorderColor is not null) values.Add(BorderColor.Value.Value);
        if (BorderWidth is not null)
        {
            if (BorderColor is null) values.Add(OeplJsonColor.Black.Value);
            values.Add(BorderWidth.Value);
        }

        return new Dictionary<string, object> { ["box"] = values };
    }
}

public sealed record JsonRoundedBoxCommand(
    int X,
    int Y,
    int Width,
    int Height,
    int CornerRadius,
    OeplJsonColor Fill,
    OeplJsonColor? BorderColor = null,
    int? BorderWidth = null) : IOeplJsonCommand
{
    public object ToPayload()
    {
        var values = new List<object> { X, Y, Width, Height, CornerRadius, Fill.Value };
        if (BorderColor is not null) values.Add(BorderColor.Value.Value);
        if (BorderWidth is not null)
        {
            if (BorderColor is null) values.Add(OeplJsonColor.Black.Value);
            values.Add(BorderWidth.Value);
        }

        return new Dictionary<string, object> { ["rbox"] = values };
    }
}

public sealed record JsonLineCommand(int X1, int Y1, int X2, int Y2, OeplJsonColor Color) : IOeplJsonCommand
{
    public object ToPayload() => new Dictionary<string, object> { ["line"] = new object[] { X1, Y1, X2, Y2, Color.Value } };
}

public sealed record JsonTriangleCommand(int X1, int Y1, int X2, int Y2, int X3, int Y3, OeplJsonColor Color) : IOeplJsonCommand
{
    public object ToPayload() => new Dictionary<string, object> { ["triangle"] = new object[] { X1, Y1, X2, Y2, X3, Y3, Color.Value } };
}

public sealed record JsonCircleCommand(int X, int Y, int Radius, OeplJsonColor Color) : IOeplJsonCommand
{
    public object ToPayload() => new Dictionary<string, object> { ["circle"] = new object[] { X, Y, Radius, Color.Value } };
}

public sealed record JsonImageCommand(string FileName, int X, int Y) : IOeplJsonCommand
{
    public object ToPayload() => new Dictionary<string, object> { ["image"] = new object[] { FileName, X, Y } };
}
