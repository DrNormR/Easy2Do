using System;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class TextAttachmentWindow : Window
{
    public string AttachmentText { get; private set; } = string.Empty;
    public bool IsSaved { get; private set; }
    public bool IsDeleted { get; private set; }

    private static readonly Regex NumberedPrefixRegex = new(@"^(\d+)\.\s", RegexOptions.Compiled);
    private const string BulletPrefix = "\u2022 ";

    public TextAttachmentWindow()
    {
        InitializeComponent();
        EditorTextBox.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
    }

    public TextAttachmentWindow(string existingText) : this()
    {
        AttachmentText = existingText;
        EditorTextBox.Text = existingText;
    }

    private void OnBulletClick(object? sender, RoutedEventArgs e)
    {
        InsertListPrefix(BulletPrefix);
    }

    private void OnNumberedClick(object? sender, RoutedEventArgs e)
    {
        InsertListPrefix("1. ");
    }

    private void InsertListPrefix(string prefix)
    {
        var caretIndex = EditorTextBox.CaretIndex;
        var text = EditorTextBox.Text ?? string.Empty;

        var lineStart = caretIndex > 0 ? text.LastIndexOf('\n', caretIndex - 1) + 1 : 0;

        if (caretIndex == lineStart || string.IsNullOrWhiteSpace(text[lineStart..caretIndex]))
        {
            EditorTextBox.Text = text.Insert(lineStart, prefix);
            EditorTextBox.CaretIndex = lineStart + prefix.Length;
        }
        else
        {
            var insertion = "\n" + prefix;
            EditorTextBox.Text = text.Insert(caretIndex, insertion);
            EditorTextBox.CaretIndex = caretIndex + insertion.Length;
        }

        EditorTextBox.Focus();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var text = EditorTextBox.Text ?? string.Empty;
        var caretIndex = EditorTextBox.CaretIndex;

        var lineStart = caretIndex > 0 ? text.LastIndexOf('\n', caretIndex - 1) + 1 : 0;
        var lineText = text[lineStart..caretIndex];

        // Check for bullet prefix
        if (lineText.StartsWith(BulletPrefix))
        {
            var content = lineText[BulletPrefix.Length..];
            if (string.IsNullOrWhiteSpace(content))
            {
                // Empty bullet line — remove it and stop the list
                e.Handled = true;
                EditorTextBox.Text = text[..lineStart] + text[caretIndex..];
                EditorTextBox.CaretIndex = lineStart;
            }
            else
            {
                // Continue the bullet list
                e.Handled = true;
                var insertion = "\n" + BulletPrefix;
                EditorTextBox.Text = text.Insert(caretIndex, insertion);
                EditorTextBox.CaretIndex = caretIndex + insertion.Length;
            }
            return;
        }

        // Check for numbered prefix
        var match = NumberedPrefixRegex.Match(lineText);
        if (match.Success)
        {
            var content = lineText[match.Length..];
            if (string.IsNullOrWhiteSpace(content))
            {
                // Empty numbered line — remove it and stop the list
                e.Handled = true;
                EditorTextBox.Text = text[..lineStart] + text[caretIndex..];
                EditorTextBox.CaretIndex = lineStart;
            }
            else
            {
                // Continue with next number
                var nextNumber = int.Parse(match.Groups[1].Value) + 1;
                var insertion = "\n" + nextNumber + ". ";
                e.Handled = true;
                EditorTextBox.Text = text.Insert(caretIndex, insertion);
                EditorTextBox.CaretIndex = caretIndex + insertion.Length;
            }
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        AttachmentText = EditorTextBox.Text ?? string.Empty;
        IsSaved = true;
        Close();
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        AttachmentText = string.Empty;
        IsDeleted = true;
        IsSaved = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsSaved = false;
        Close();
    }
}
