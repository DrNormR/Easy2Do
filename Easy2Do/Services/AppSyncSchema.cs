using System.Collections.Generic;
using PowerSync.Common.DB.Schema;

namespace Easy2Do.Services;

public static class AppSyncSchema
{
    public static readonly Schema Schema = new(new Dictionary<string, Table>
    {
        ["notes"] = new Table(
            new Dictionary<string, ColumnType>
            {
                ["id"] = ColumnType.TEXT,
                ["title"] = ColumnType.TEXT,
                ["color"] = ColumnType.TEXT,
                ["created_date"] = ColumnType.TEXT,
                ["modified_date"] = ColumnType.TEXT,
                ["window_x"] = ColumnType.REAL,
                ["window_y"] = ColumnType.REAL,
                ["window_width"] = ColumnType.REAL,
                ["window_height"] = ColumnType.REAL,
                ["is_pinned"] = ColumnType.INTEGER
            },
            new TableOptions(null, null, null, null, null, null, null)),
        ["note_items"] = new Table(
            new Dictionary<string, ColumnType>
            {
                ["id"] = ColumnType.TEXT,
                ["note_id"] = ColumnType.TEXT,
                ["text"] = ColumnType.TEXT,
                ["is_completed"] = ColumnType.INTEGER,
                ["is_heading"] = ColumnType.INTEGER,
                ["is_important"] = ColumnType.INTEGER,
                ["text_attachment"] = ColumnType.TEXT,
                ["due_date"] = ColumnType.TEXT,
                ["is_alarm_dismissed"] = ColumnType.INTEGER,
                ["snooze_until"] = ColumnType.TEXT,
                ["created_at_utc"] = ColumnType.TEXT,
                ["updated_at_utc"] = ColumnType.TEXT,
                ["deleted_at_utc"] = ColumnType.TEXT,
                ["position"] = ColumnType.INTEGER
            },
            new TableOptions(null, null, null, null, null, null, null)),
        ["note_order"] = new Table(
            new Dictionary<string, ColumnType>
            {
                ["id"] = ColumnType.TEXT,
                ["note_id"] = ColumnType.TEXT,
                ["sort_order"] = ColumnType.INTEGER
            },
            new TableOptions(null, null, null, null, null, null, null))
    });
}
