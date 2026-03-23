using WorkshopAssignment.Models;
using WorkshopAssignment.Services;
using Xunit;

namespace WorkshopAssignment.Tests;

/// <summary>
/// Tests for workshop data alteration validation per DATA_ALTERATION_SPEC.md.
/// Validates: capacity, min capacity, type, location, and source file tracking.
/// </summary>
public class DataAlterationTests
{
    private readonly DataAlterationValidator _validator = new();
    private readonly WorkshopEditTracker _tracker = new();

    // ---------------------------------------------------------------
    //  Helper: quick workshop constructor
    // ---------------------------------------------------------------
    private static Workshop W(string id, WorkshopType type, int cap = 20, int minCap = 5, string? location = null, string? sourceFile = null)
        => new(id, $"Workshop {id}", type, cap, minCap, location, sourceFile);

    // ---------------------------------------------------------------
    //  1. WorkshopEdit_CapacityZero_Valid
    //     Per spec: Setting capacity to 0 is valid (workshop disabled)
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_CapacityZero_Valid()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, cap: 20, minCap: 5);

        // Act
        var result = _validator.ApplyEdits(original, newCapacity: 0, newMinCapacity: 0, newType: original.Type, newLocation: original.Location);

        // Assert
        Assert.Equal(0, result.Capacity);
        // Workshop is now disabled but valid
    }

    // ---------------------------------------------------------------
    //  2. WorkshopEdit_CapacityNegative_BecomesZero
    //     Per spec: Negative capacity should be corrected to 0
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_CapacityNegative_BecomesZero()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, cap: 20, minCap: 5);

        // Act
        var result = _validator.ApplyEdits(original, newCapacity: -10, newMinCapacity: 5, newType: original.Type, newLocation: original.Location);

        // Assert
        Assert.Equal(0, result.Capacity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void WorkshopEdit_CapacityNegative_AllBecome0(int negativeCapacity)
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2);

        // Act
        var result = _validator.ApplyEdits(original, newCapacity: negativeCapacity, newMinCapacity: 0, newType: original.Type, newLocation: original.Location);

        // Assert
        Assert.Equal(0, result.Capacity);
    }

    // ---------------------------------------------------------------
    //  3. WorkshopEdit_MinGreaterThanCapacity_MinAdjusted
    //     Per spec: If min > capacity after edit, min should become = capacity
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_MinGreaterThanCapacity_MinAdjusted()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, cap: 20, minCap: 5);

        // Act - setting capacity to 10 but min to 15
        var result = _validator.ApplyEdits(original, newCapacity: 10, newMinCapacity: 15, newType: original.Type, newLocation: original.Location);

        // Assert - min should be clamped to capacity
        Assert.Equal(10, result.Capacity);
        Assert.Equal(10, result.MinCapacity);
    }

    [Fact]
    public void WorkshopEdit_CapacityZeroWithMinGreaterThanZero_MinBecomes0()
    {
        // Arrange - per spec: "Set to 0 while min > 0 - Fine (both effectively 0)"
        var original = W("W1", WorkshopType.Type2, cap: 20, minCap: 5);

        // Act - setting capacity to 0 but min to 5
        var result = _validator.ApplyEdits(original, newCapacity: 0, newMinCapacity: 5, newType: original.Type, newLocation: original.Location);

        // Assert - min should be clamped to capacity (0)
        Assert.Equal(0, result.Capacity);
        Assert.Equal(0, result.MinCapacity);
    }

    // ---------------------------------------------------------------
    //  4. WorkshopEdit_MinNegative_BecomesZero
    //     Per spec: Negative min should be corrected to 0
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_MinNegative_BecomesZero()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, cap: 20, minCap: 5);

        // Act
        var result = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: -5, newType: original.Type, newLocation: original.Location);

        // Assert
        Assert.Equal(0, result.MinCapacity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void WorkshopEdit_MinNegative_AllBecome0(int negativeMin)
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, cap: 20);

        // Act
        var result = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: negativeMin, newType: original.Type, newLocation: original.Location);

        // Assert
        Assert.Equal(0, result.MinCapacity);
    }

    // ---------------------------------------------------------------
    //  5. WorkshopEdit_LocationEmpty_Valid
    //     Per spec: Empty location is valid (optional field)
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_LocationEmpty_Valid()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, location: "Room 101");

        // Act - setting location to empty
        var result = _validator.ApplyEdits(original, newCapacity: original.Capacity, newMinCapacity: original.MinCapacity, newType: original.Type, newLocation: "");

        // Assert
        Assert.Equal("", result.Location);
        Assert.True(_validator.IsValidLocation(result.Location));
    }

    [Fact]
    public void WorkshopEdit_LocationNull_Valid()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, location: "Room 101");

        // Act - setting location to null
        var result = _validator.ApplyEdits(original, newCapacity: original.Capacity, newMinCapacity: original.MinCapacity, newType: original.Type, newLocation: null);

        // Assert
        Assert.Null(result.Location);
        Assert.True(_validator.IsValidLocation(result.Location));
    }

    [Fact]
    public void WorkshopEdit_LocationAnyString_Valid()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, location: null);

        // Act - setting location to any string
        var result = _validator.ApplyEdits(original, newCapacity: original.Capacity, newMinCapacity: original.MinCapacity, newType: original.Type, newLocation: "Building A, Floor 3, Room 301");

        // Assert
        Assert.Equal("Building A, Floor 3, Room 301", result.Location);
        Assert.True(_validator.IsValidLocation(result.Location));
    }

    // ---------------------------------------------------------------
    //  6. WorkshopEdit_TypeChange_Valid
    //     Per spec: Changing type to any valid type works
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_TypeChange_Valid()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2);

        // Act - change to each valid type
        var resultType1 = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: 5, newType: WorkshopType.Type1, newLocation: null);
        var resultType2 = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: 5, newType: WorkshopType.Type2, newLocation: null);
        var resultType3 = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: 5, newType: WorkshopType.Type3, newLocation: null);
        var resultType4 = _validator.ApplyEdits(original, newCapacity: 20, newMinCapacity: 5, newType: WorkshopType.Type4, newLocation: null);

        // Assert - all types are valid
        Assert.Equal(WorkshopType.Type1, resultType1.Type);
        Assert.Equal(WorkshopType.Type2, resultType2.Type);
        Assert.Equal(WorkshopType.Type3, resultType3.Type);
        Assert.Equal(WorkshopType.Type4, resultType4.Type);

        Assert.True(_validator.IsValidType(resultType1.Type));
        Assert.True(_validator.IsValidType(resultType2.Type));
        Assert.True(_validator.IsValidType(resultType3.Type));
        Assert.True(_validator.IsValidType(resultType4.Type));
    }

    [Theory]
    [InlineData(WorkshopType.Type1)]
    [InlineData(WorkshopType.Type2)]
    [InlineData(WorkshopType.Type3)]
    [InlineData(WorkshopType.Type4)]
    public void WorkshopEdit_AllValidTypes_Accepted(WorkshopType type)
    {
        Assert.True(_validator.IsValidType(type));
    }

    // ---------------------------------------------------------------
    //  7. WorkshopEdit_SourceFileTracked
    //     Per spec: Workshop remembers its source file
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_SourceFileTracked()
    {
        // Arrange
        var original = W("W1", WorkshopType.Type2, sourceFile: "workshops.xlsx");

        // Act - edit the workshop
        var result = _validator.ApplyEdits(original, newCapacity: 25, newMinCapacity: 10, newType: WorkshopType.Type3, newLocation: "Room 102");

        // Assert - source file is preserved through edit
        Assert.Equal("workshops.xlsx", result.SourceFile);
    }

    [Fact]
    public void WorkshopEdit_SourceFileTracked_InEditTracker()
    {
        // Arrange
        var edit = new WorkshopEdit("W1", Capacity: 25, SourceFile: "workshops.xlsx");

        // Act
        _tracker.RecordEdit(edit);
        var retrieved = _tracker.GetEdit("W1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("workshops.xlsx", retrieved.SourceFile);
    }

    // ---------------------------------------------------------------
    //  8. FileRemoval_EditsFromRemovedFile_Discarded
    //     Per spec: When file X is removed, workshops from file X lose their edits
    // ---------------------------------------------------------------
    [Fact]
    public void FileRemoval_EditsFromRemovedFile_Discarded()
    {
        // Arrange - create edits for workshops from different files
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 30, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W2", Capacity: 25, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W3", Capacity: 20, SourceFile: "fileB.xlsx"));

        // Act - remove fileA
        var removedIds = _tracker.RemoveEditsForSourceFile("fileA.xlsx");

        // Assert - W1 and W2 edits should be removed
        Assert.Contains("W1", removedIds);
        Assert.Contains("W2", removedIds);
        Assert.DoesNotContain("W3", removedIds);

        Assert.Null(_tracker.GetEdit("W1"));
        Assert.Null(_tracker.GetEdit("W2"));
    }

    [Fact]
    public void FileRemoval_NoMatchingEdits_NothingRemoved()
    {
        // Arrange
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 30, SourceFile: "fileA.xlsx"));

        // Act - remove a file that has no edits
        var removedIds = _tracker.RemoveEditsForSourceFile("nonexistent.xlsx");

        // Assert
        Assert.Empty(removedIds);
        Assert.NotNull(_tracker.GetEdit("W1"));
    }

    // ---------------------------------------------------------------
    //  9. FileRemoval_EditsFromOtherFile_Preserved
    //     Per spec: When file X is removed, workshops from file Y keep their edits
    // ---------------------------------------------------------------
    [Fact]
    public void FileRemoval_EditsFromOtherFile_Preserved()
    {
        // Arrange - create edits for workshops from different files
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 30, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W2", Capacity: 25, SourceFile: "fileB.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W3", Capacity: 20, SourceFile: "fileB.xlsx"));

        // Act - remove fileA
        _tracker.RemoveEditsForSourceFile("fileA.xlsx");

        // Assert - W2 and W3 edits from fileB should be preserved
        Assert.NotNull(_tracker.GetEdit("W2"));
        Assert.NotNull(_tracker.GetEdit("W3"));
        Assert.Equal(25, _tracker.GetEdit("W2")!.Capacity);
        Assert.Equal(20, _tracker.GetEdit("W3")!.Capacity);
    }

    [Fact]
    public void FileRemoval_MultipleSameFile_AllRemoved()
    {
        // Arrange - multiple edits from the same file
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 10, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W2", Capacity: 20, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W3", Capacity: 30, SourceFile: "fileA.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W4", Capacity: 40, SourceFile: "fileB.xlsx"));

        // Act
        var removedIds = _tracker.RemoveEditsForSourceFile("fileA.xlsx");

        // Assert
        Assert.Equal(3, removedIds.Count);
        Assert.Contains("W1", removedIds);
        Assert.Contains("W2", removedIds);
        Assert.Contains("W3", removedIds);

        // fileB edit preserved
        Assert.NotNull(_tracker.GetEdit("W4"));
        Assert.Equal(40, _tracker.GetEdit("W4")!.Capacity);
    }

    // ---------------------------------------------------------------
    //  Additional edge case tests
    // ---------------------------------------------------------------
    [Fact]
    public void WorkshopEdit_PreservesOtherFields()
    {
        // Arrange
        var original = new Workshop("W1", "Original Name", WorkshopType.Type2, 20, 5, "Room 1", "source.xlsx");

        // Act - only change capacity
        var result = _validator.ApplyEdits(original, newCapacity: 30, newMinCapacity: 5, newType: original.Type, newLocation: original.Location);

        // Assert - other fields preserved
        Assert.Equal("W1", result.Id);
        Assert.Equal("Original Name", result.Name);
        Assert.Equal("Room 1", result.Location);
        Assert.Equal("source.xlsx", result.SourceFile);
    }

    [Fact]
    public void ValidateCapacity_DirectMethod_ReturnsCorrectValue()
    {
        Assert.Equal(0, _validator.ValidateCapacity(-5));
        Assert.Equal(0, _validator.ValidateCapacity(0));
        Assert.Equal(10, _validator.ValidateCapacity(10));
        Assert.Equal(int.MaxValue, _validator.ValidateCapacity(int.MaxValue));
    }

    [Fact]
    public void ValidateMinCapacity_DirectMethod_ReturnsCorrectValue()
    {
        Assert.Equal(0, _validator.ValidateMinCapacity(-5));
        Assert.Equal(0, _validator.ValidateMinCapacity(0));
        Assert.Equal(10, _validator.ValidateMinCapacity(10));
    }

    [Fact]
    public void AdjustMinCapacityToCapacity_DirectMethod_ClampsCorrectly()
    {
        // Min > capacity should clamp
        Assert.Equal(10, _validator.AdjustMinCapacityToCapacity(15, 10));

        // Min = capacity is fine
        Assert.Equal(10, _validator.AdjustMinCapacityToCapacity(10, 10));

        // Min < capacity is fine
        Assert.Equal(5, _validator.AdjustMinCapacityToCapacity(5, 10));

        // Capacity 0 case
        Assert.Equal(0, _validator.AdjustMinCapacityToCapacity(5, 0));
    }

    [Fact]
    public void WorkshopEditTracker_Clear_RemovesAllEdits()
    {
        // Arrange
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 10, SourceFile: "file.xlsx"));
        _tracker.RecordEdit(new WorkshopEdit("W2", Capacity: 20, SourceFile: "file.xlsx"));

        // Act
        _tracker.Clear();

        // Assert
        Assert.Null(_tracker.GetEdit("W1"));
        Assert.Null(_tracker.GetEdit("W2"));
        Assert.Empty(_tracker.GetAllEdits());
    }

    [Fact]
    public void WorkshopEditTracker_RecordEdit_OverwritesPrevious()
    {
        // Arrange
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 10, SourceFile: "file.xlsx"));

        // Act - record a new edit for same workshop
        _tracker.RecordEdit(new WorkshopEdit("W1", Capacity: 20, SourceFile: "file.xlsx"));

        // Assert - should have the new value
        var edit = _tracker.GetEdit("W1");
        Assert.NotNull(edit);
        Assert.Equal(20, edit.Capacity);
    }
}
