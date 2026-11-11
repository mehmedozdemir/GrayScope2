"""Seeds a GrayScope 2 database with demo profiles/folders/queries pointing at mock_graylog.py.

Usage: python -I seed_demo.py <path-to-grayscope2.db> <dpapi-protected-token>
The database must already exist (start the app once so migrations run).
"""
import json
import sqlite3
import sys

db_path, token = sys.argv[1], sys.argv[2]
now = "2026-01-01T08:00:00.000Z"
c = sqlite3.connect(db_path)
c.execute("PRAGMA foreign_keys=ON")
if c.execute("SELECT COUNT(*) FROM GraylogProfile").fetchone()[0]:
    print("database already has profiles; nothing to do")
    sys.exit(0)

for name, default in (("Ana Sunucu (mock)", 1), ("Yedek Sunucu (mock)", 0)):
    c.execute(
        "INSERT INTO GraylogProfile (Name,BaseUrl,TokenProtected,IsDefault,IsActive,CreatedAt,UpdatedAt) VALUES (?,?,?,?,1,?,?)",
        (name, "http://127.0.0.1:9123/api", token, default, now, now),
    )
c.execute("INSERT INTO QueryFolder (Name,ParentId) VALUES ('Operasyon',NULL)")
c.execute("INSERT INTO QueryFolder (Name,ParentId) VALUES ('HTTP',1)")


def query(name, folder, template, fields, streams):
    cur = c.execute(
        """INSERT INTO SavedQuery (Name,ProfileId,FolderId,Template,TimeRangeKind,RangeSeconds,FieldsJson,ResultSize,CreatedAt,UpdatedAt,Description)
           VALUES (?,1,?,?,'Relative',3600,?,150,?,?,'Mock veriyle örnek sorgu')""",
        (name, folder, template, json.dumps(fields), now, now),
    )
    for sid, sname in streams:
        c.execute("INSERT INTO QueryStream (QueryId,StreamId,StreamName) VALUES (?,?,?)", (cur.lastrowid, sid, sname))


query("Şehir bazlı 200 dışı yanıtlar", 2, "NetworkId:{NetworkId} AND NOT StatusCode:200",
      ["timestamp", "source", "StatusCode", "Path", "DurationMs", "message"], [("s-app", "Uygulama")])
query("Sunucu hataları", 1, "StatusCode:500 AND source:{Kaynak?}", [], [("s-gw", "API Gateway")])
query("Tüm istekler", None, "*", ["timestamp", "NetworkId", "Path", "StatusCode"], [])
c.commit()
print("seeded demo data")
