using GitDesk.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace GitDesk.ViewModels;

public sealed class MergeConflictDialogViewModel : ObservableObject
{
    private string _workingContent;
    private MergeConflictBlock? _selectedConflictBlock;

    public MergeConflictDialogViewModel(MergeConflictFile file)
    {
        File = file;
        _workingContent = file.WorkingContent;
        RebuildConflictBlocks();
        SelectedConflictBlock = ConflictBlocks.FirstOrDefault();
    }

    public MergeConflictFile File { get; }

    public ObservableCollection<MergeConflictBlock> ConflictBlocks { get; } = new();

    public MergeConflictBlock? SelectedConflictBlock
    {
        get => _selectedConflictBlock;
        set
        {
            if (SetProperty(ref _selectedConflictBlock, value))
            {
                OnPropertyChanged(nameof(SelectedConflictText));
            }
        }
    }

    public string TitleText => $"Merge Conflict - {File.Path}";

    public string SummaryText => $"{File.Details} ({File.Status})";

    public string MergeSummaryText => ConflictBlocks.Count == 0
        ? "All conflict markers are resolved. Review the result, then Save or Mark Resolved."
        : $"{ConflictBlocks.Count} conflict block(s). Select a block and apply Ours, Theirs, or Both to the result.";

    public string SelectedConflictText => SelectedConflictBlock is null
        ? "No conflict block selected."
        : $"Selected conflict #{SelectedConflictBlock.Number}";

    public string BaseHeaderText => $"Base: {File.Path}";

    public string OursHeaderText => $"Ours: {File.Path}";

    public string TheirsHeaderText => $"Theirs: {File.Path}";

    public string WorkingHeaderText => $"Merged Result: {File.Path}";

    public string BaseContent => File.BaseContent;

    public string OursContent => File.OursContent;

    public string TheirsContent => File.TheirsContent;

    public string WorkingContent
    {
        get => _workingContent;
        set
        {
            if (SetProperty(ref _workingContent, value))
            {
                RebuildConflictBlocks();
            }
        }
    }

    public void ApplySelectedOurs()
    {
        ApplySelectedChoice(MergeChoice.Ours);
    }

    public void ApplySelectedTheirs()
    {
        ApplySelectedChoice(MergeChoice.Theirs);
    }

    public void ApplySelectedBoth()
    {
        ApplySelectedChoice(MergeChoice.Both);
    }

    public void ApplyAllOurs()
    {
        ApplyAllChoice(MergeChoice.Ours);
    }

    public void ApplyAllTheirs()
    {
        ApplyAllChoice(MergeChoice.Theirs);
    }

    public void ApplyAllBoth()
    {
        ApplyAllChoice(MergeChoice.Both);
    }

    public void SelectPreviousConflict()
    {
        if (ConflictBlocks.Count == 0)
        {
            return;
        }

        var index = SelectedConflictBlock is null ? 0 : ConflictBlocks.IndexOf(SelectedConflictBlock) - 1;
        SelectedConflictBlock = ConflictBlocks[Math.Clamp(index, 0, ConflictBlocks.Count - 1)];
    }

    public void SelectNextConflict()
    {
        if (ConflictBlocks.Count == 0)
        {
            return;
        }

        var index = SelectedConflictBlock is null ? 0 : ConflictBlocks.IndexOf(SelectedConflictBlock) + 1;
        SelectedConflictBlock = ConflictBlocks[Math.Clamp(index, 0, ConflictBlocks.Count - 1)];
    }

    public void UseWholeOursFile()
    {
        WorkingContent = OursContent;
    }

    public void UseWholeTheirsFile()
    {
        WorkingContent = TheirsContent;
    }

    private void ApplySelectedChoice(MergeChoice choice)
    {
        if (SelectedConflictBlock is null)
        {
            return;
        }

        var selectedNumber = SelectedConflictBlock.Number;
        _workingContent = ResolveConflictMarkers(WorkingContent, choice, selectedNumber);
        OnPropertyChanged(nameof(WorkingContent));
        RebuildConflictBlocks();
        SelectedConflictBlock = ConflictBlocks.FirstOrDefault(block => block.Number >= selectedNumber) ??
                                ConflictBlocks.LastOrDefault();
    }

    private void ApplyAllChoice(MergeChoice choice)
    {
        WorkingContent = ConflictBlocks.Count == 0
            ? choice switch
            {
                MergeChoice.Ours => OursContent,
                MergeChoice.Theirs => TheirsContent,
                MergeChoice.Both => $"{TrimTrailingNewLine(OursContent)}{Environment.NewLine}{TrimTrailingNewLine(TheirsContent)}{Environment.NewLine}",
                _ => WorkingContent,
            }
            : ResolveConflictMarkers(WorkingContent, choice, selectedBlockNumber: null);
    }

    private void RebuildConflictBlocks()
    {
        var previousNumber = SelectedConflictBlock?.Number;
        ConflictBlocks.Clear();
        foreach (var block in ParseConflictBlocks(WorkingContent))
        {
            ConflictBlocks.Add(block);
        }

        SelectedConflictBlock = previousNumber is null
            ? ConflictBlocks.FirstOrDefault()
            : ConflictBlocks.FirstOrDefault(block => block.Number == previousNumber) ?? ConflictBlocks.FirstOrDefault();
        OnPropertyChanged(nameof(MergeSummaryText));
    }

    private static string ResolveConflictMarkers(string content, MergeChoice choice, int? selectedBlockNumber)
    {
        var blocks = ParseConflictBlocks(content);
        if (blocks.Count == 0)
        {
            return content;
        }

        var blockByStart = blocks.ToDictionary(block => block.StartLine);
        var lines = SplitLines(content);
        var output = new List<string>();
        var index = 0;
        while (index < lines.Count)
        {
            if (!blockByStart.TryGetValue(index, out var block))
            {
                output.Add(lines[index]);
                index++;
                continue;
            }

            if (selectedBlockNumber is not null && block.Number != selectedBlockNumber.Value)
            {
                output.AddRange(lines.Skip(block.StartLine).Take(block.EndLine - block.StartLine + 1));
                index = block.EndLine + 1;
                continue;
            }

            output.AddRange(choice switch
            {
                MergeChoice.Ours => block.OursLines,
                MergeChoice.Theirs => block.TheirsLines,
                MergeChoice.Both => block.OursLines.Concat(block.TheirsLines),
                _ => Array.Empty<string>(),
            });
            index = block.EndLine + 1;
        }

        return JoinLines(output, content.EndsWith('\n'));
    }

    private static IReadOnlyList<MergeConflictBlock> ParseConflictBlocks(string content)
    {
        var blocks = new List<MergeConflictBlock>();
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                continue;
            }

            var start = index;
            index++;
            var ours = new List<string>();
            while (index < lines.Count && !lines[index].StartsWith("=======", StringComparison.Ordinal))
            {
                ours.Add(lines[index++]);
            }

            if (index >= lines.Count)
            {
                break;
            }

            index++;
            var theirs = new List<string>();
            while (index < lines.Count && !lines[index].StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                theirs.Add(lines[index++]);
            }

            if (index >= lines.Count)
            {
                break;
            }

            blocks.Add(new MergeConflictBlock(
                blocks.Count + 1,
                start,
                index,
                ours,
                theirs));
        }

        return blocks;
    }

    private static IReadOnlyList<string> SplitLines(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToArray();
    }

    private static string JoinLines(IReadOnlyList<string> lines, bool appendFinalNewline)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            if (index == lines.Count - 1 && string.IsNullOrEmpty(lines[index]) && appendFinalNewline)
            {
                continue;
            }

            builder.Append(lines[index]);
            if (index < lines.Count - 1 || appendFinalNewline)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string TrimTrailingNewLine(string text)
    {
        return text.TrimEnd('\r', '\n');
    }

    private enum MergeChoice
    {
        Ours,
        Theirs,
        Both,
    }
}

public sealed class MergeConflictBlock
{
    public MergeConflictBlock(
        int number,
        int startLine,
        int endLine,
        IReadOnlyList<string> oursLines,
        IReadOnlyList<string> theirsLines)
    {
        Number = number;
        StartLine = startLine;
        EndLine = endLine;
        OursLines = oursLines;
        TheirsLines = theirsLines;
    }

    public int Number { get; }

    public int StartLine { get; }

    public int EndLine { get; }

    public IReadOnlyList<string> OursLines { get; }

    public IReadOnlyList<string> TheirsLines { get; }

    public string DisplayName => $"Conflict {Number}";

    public string OursPreview => string.Join(" / ", OursLines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();

    public string TheirsPreview => string.Join(" / ", TheirsLines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();
}
