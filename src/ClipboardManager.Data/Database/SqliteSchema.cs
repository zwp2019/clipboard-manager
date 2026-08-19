namespace ClipboardManager.Data;

/// <summary>
/// SQLite Schema 定义与迁移脚本（规范 §17、§16 Migration）。
/// </summary>
public static class SqliteSchema
{
    public const int CurrentVersion = 1;

    public static readonly IReadOnlyList<Migration> Migrations = new List<Migration>
    {
        new(1, V1),
    };

    private const string V1 =
        """
        CREATE TABLE IF NOT EXISTS clipboard_items (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            type               INTEGER NOT NULL,
            text               TEXT,
            html               TEXT,
            rtf                TEXT,
            image_path         TEXT,
            thumbnail_path     TEXT,
            file_paths         TEXT,
            source_application TEXT,
            source_title       TEXT,
            created_at         TEXT NOT NULL,
            updated_at         TEXT NOT NULL,
            hash               TEXT NOT NULL,
            size               INTEGER NOT NULL DEFAULT 0,
            is_favorite        INTEGER NOT NULL DEFAULT 0,
            is_deleted         INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_clipboard_items_created ON clipboard_items (created_at DESC, id DESC);
        CREATE INDEX IF NOT EXISTS idx_clipboard_items_hash ON clipboard_items (hash);
        CREATE INDEX IF NOT EXISTS idx_clipboard_items_favorite ON clipboard_items (is_favorite, created_at DESC);

        -- FTS5 外部内容表（规范 §18）
        CREATE TABLE IF NOT EXISTS clipboard_search (
            id      INTEGER PRIMARY KEY,
            content TEXT NOT NULL DEFAULT ''
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS clipboard_search_fts USING fts5(
            content,
            content='clipboard_search',
            content_rowid='id',
            tokenize='unicode61'
        );

        -- 删除条目时联动清理搜索索引
        CREATE TRIGGER IF NOT EXISTS trg_items_after_delete
        AFTER DELETE ON clipboard_items
        BEGIN
            DELETE FROM clipboard_search WHERE id = old.id;
        END;

        -- 搜索内容表 -> FTS 同步触发器
        CREATE TRIGGER IF NOT EXISTS trg_search_after_insert
        AFTER INSERT ON clipboard_search
        BEGIN
            INSERT INTO clipboard_search_fts (rowid, content) VALUES (new.id, new.content);
        END;

        CREATE TRIGGER IF NOT EXISTS trg_search_after_delete
        AFTER DELETE ON clipboard_search
        BEGIN
            INSERT INTO clipboard_search_fts (clipboard_search_fts, rowid, content)
            VALUES ('delete', old.id, old.content);
        END;

        CREATE TRIGGER IF NOT EXISTS trg_search_after_update
        AFTER UPDATE ON clipboard_search
        BEGIN
            INSERT INTO clipboard_search_fts (clipboard_search_fts, rowid, content)
            VALUES ('delete', old.id, old.content);
            INSERT INTO clipboard_search_fts (rowid, content) VALUES (new.id, new.content);
        END;

        -- 标签（第二阶段）
        CREATE TABLE IF NOT EXISTS tags (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS clipboard_tags (
            clipboard_item_id INTEGER NOT NULL,
            tag_id            INTEGER NOT NULL,
            PRIMARY KEY (clipboard_item_id, tag_id),
            FOREIGN KEY (clipboard_item_id) REFERENCES clipboard_items (id) ON DELETE CASCADE,
            FOREIGN KEY (tag_id) REFERENCES tags (id) ON DELETE CASCADE
        );

        -- 设置
        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;
}

public sealed record Migration(int Version, string Sql);
