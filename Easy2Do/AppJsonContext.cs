using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Easy2Do.Models;
using Easy2Do.Services;

namespace Easy2Do;

/// <summary>
/// Source-generated JSON serializer context.
/// Registers every type that is serialized/deserialized by StorageService and BackupService,
/// so that System.Text.Json does NOT need runtime reflection or JIT — required for iOS AOT.
/// </summary>
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(List<Note>))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(TodoItem))]
[JsonSerializable(typeof(ObservableCollection<TodoItem>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
