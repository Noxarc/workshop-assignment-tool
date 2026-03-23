namespace WorkshopAssignment.Models;

public enum WarningCategory
{
    DuplicateId,
    SelfReference,
    InvalidFriend,
    NonMutualFriend,
    InvalidTimeslot,
    InvalidCapacity,
    CapacityAdjusted,
    UnknownWorkshop,
    InvalidWishCombo,
    IncompleteWish,
    EmptyWish,
    EmptySheet,
    EmptyWorkshopName,
    EmptyPersonName,
    WhitespaceId,
    OversizedGroup,
    PersonSkipped,      // Person excluded entirely due to critical errors
    WorkshopSkipped,    // Workshop excluded due to critical errors (e.g., invalid type)

    // Cross-file workshop validation warnings
    WorkshopDataMismatch,   // Persons file has different data for a workshop that exists in workshops file
    MissingWorkshops,       // Persons file is missing workshops that exist in workshops file
    ExtraWorkshopIgnored    // Persons file has workshops not in workshops file (ignored)
}

public record ImportWarning(
    WarningCategory Category,
    string Message
);

public record ImportResult(
    AssignmentInput InputData,
    List<ImportWarning> Warnings
)
{
    public bool CanRunSolver =>
        InputData?.Workshops?.Count > 0 &&
        InputData?.Persons?.Count > 0 &&
        InputData?.FriendGroups?.Count > 0;
}
