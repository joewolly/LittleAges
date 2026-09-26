namespace LittleAges.Domain;

public enum RoadGrade : byte { None = 0, Track = 1, Trail = 2, Road = 3 }

/// <summary>Per-tile M15 road grades over the immutable map. Derived from canonical road state; never checkpointed itself.</summary>
public sealed class RoadGradeMap
{
    private readonly RoadGrade[] _grades;

    public RoadGradeMap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _grades = new RoadGrade[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public RoadGrade GradeAt(TileCoordinate coordinate) => _grades[Index(coordinate)];

    public void SetGrade(TileCoordinate coordinate, RoadGrade grade)
    {
        if (!Enum.IsDefined(grade)) throw new ArgumentOutOfRangeException(nameof(grade));
        _grades[Index(coordinate)] = grade;
    }

    private int Index(TileCoordinate coordinate)
    {
        if (coordinate.X < 0 || coordinate.X >= Width || coordinate.Y < 0 || coordinate.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return coordinate.Y * Width + coordinate.X;
    }
}
