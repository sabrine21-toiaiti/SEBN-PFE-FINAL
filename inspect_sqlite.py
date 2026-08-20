import sqlite3
path = r'C:/Users/asus/Desktop/app pfe/SEBN-DOTNET/SebnWeb/Data/sebn.db'
conn = sqlite3.connect(path)
cur = conn.cursor()
print('TABLES', [row[0] for row in cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")])
print('FK', cur.execute('PRAGMA foreign_keys').fetchone()[0])
for name, in cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"):
    cnt = cur.execute(f"SELECT COUNT(*) FROM \"{name}\"").fetchone()[0]
    print(name, cnt)
    print('COLUMNS', [col[1:] for col in cur.execute(f"PRAGMA table_info('{name}')").fetchall()])
conn.close()
