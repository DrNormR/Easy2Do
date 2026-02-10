using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class TextAttachmentWindow : Window
{
    public string AttachmentText { get; private set; } = string.Empty;
    public bool IsSaved { get; private set; }

    public TextAttachmentWindow()
    {
        InitializeComponent();
    }

    public TextAttachmentWindow(string existingText) : this()
    {
        AttachmentText = existingText;
        EditorTextBox.Text = existingText;
    }

    private void OnBulletClick(object? sender, RoutedEventArgs e)
    {
        InsertListPrefix("\u2022 ");
    }

    private void OnNumberedClick(object? sender, RoutedEventArgs e)
    {
        var text = EditorTextBox.Text ?? string.Empty;
        var lines = text.Split('\n');
        var numberedCount = lines.Count(l => l.TrimStart().Length > 0 &&
            l.TrimStart().Length > 1 &&
            char.IsDigit(l.TrimStart()[0]));

        var nextNumber = numberedCount + 1;
        InsertListPrefix($"{nextNumber}. ");
    }

    private void InsertListPrefix(string prefix)
    {
        var caretIndex = EditorTextBox.CaretIndex;
        var text = EditorTextBox.Text ?? string.Empty;

        // Find the start of the current line
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        // If we're at the start of a line or the line is empty, just insert the prefix
        if (caretIndex == lineStart || string.IsNullOrWhiteSpace(text[lineStart..caretIndex]))
        {
            EditorTextBox.Text = text.Insert(lineStart, prefix);
            EditorTextBox.CaretIndex = lineStart + prefix.Length;
        }
        else
        {
            // Insert a new line with the prefix
            var insertion = Environment.NewLine + prefix;
            EditorTextBox.Text = text.Insert(caretIndex, insertion);
            EditorTextBox.CaretIndex = caretIndex + insertion.Length;
        }

        EditorTextBox.Focus();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        AttachmentText = EditorTextBox.Text ?? string.Empty;
        IsSaved = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsSaved = false;
        Close();
    }
}
