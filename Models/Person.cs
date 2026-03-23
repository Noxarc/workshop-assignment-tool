namespace WorkshopAssignment.Models;

public record Person(
    string Id,
    string Name,
    List<string> Preferences,
    string? FriendId = null,
    string? Info = null,
    string? SourceFile = null
);

public record FriendGroup(
    List<string> MemberIds,
    List<string> PooledPreferences
);
