namespace WorkshopAssignment.Models;

public record AssignmentInput(
    List<Workshop> Workshops,
    List<Person> Persons,
    List<FriendGroup> FriendGroups
);
