using System.Text.Json;

namespace OEPLLib;

public sealed class JsonTemplateDocument
{
    private readonly List<IOeplJsonCommand> _commands = [];

    public IReadOnlyList<IOeplJsonCommand> Commands => _commands;

    public JsonTemplateDocument Add(IOeplJsonCommand command)
    {
        _commands.Add(command);
        return this;
    }

    public string ToJson()
    {
        var payload = _commands.Select(command => command.ToPayload()).ToArray();
        return JsonSerializer.Serialize(payload);
    }
}

public interface IOeplJsonCommand
{
    object ToPayload();
}

public readonly record struct OeplJsonColor(string Value)
{
    public static readonly OeplJsonColor White = new("white");
    public static readonly OeplJsonColor Black = new("black");
    public static readonly OeplJsonColor Red = new("red");
    public static readonly OeplJsonColor Yellow = new("yellow");
}

public enum OeplJsonTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}
