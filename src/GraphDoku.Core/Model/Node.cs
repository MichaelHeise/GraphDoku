namespace GraphDoku.Core.Model;

/// <summary>Ein Knoten (eine Zelle) des Rätsels an einer ganzzahligen Gitterposition.</summary>
public sealed class Node
{
    public Node(int id, int x, int y, int? given = null, ValueDomain? domain = null)
    {
        Id = id;
        X = x;
        Y = y;
        Given = given;
        Domain = domain;
    }

    public int Id { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Optionaler eigener Wertebereich; sonst gilt der globale Bereich des Rätsels.</summary>
    public ValueDomain? Domain { get; set; }

    /// <summary>Optional vorgegebener Wert.</summary>
    public int? Given { get; set; }

    public Node Clone() => new(Id, X, Y, Given, Domain);

    public override string ToString() => $"Knoten {Id} ({X}, {Y})";
}
