namespace WorkshopAssignment.Models;

public enum WorkshopType
{
    Type1 = 1,  // Slot 1 only (standalone)
    Type2 = 2,  // Slot 2 only
    Type3 = 3,  // Slot 3 only
    Type4 = 4   // Slot 2 OR 3 (flexible)
}

public record Workshop(
    string Id,
    string Name,
    WorkshopType Type,
    int Capacity,
    int MinCapacity = 0,
    string? Location = null,
    string? SourceFile = null
);
