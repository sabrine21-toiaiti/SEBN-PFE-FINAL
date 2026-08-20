import sqlite3
import json
import os
path = r'C:/Users/asus/Desktop/app pfe/SEBN-DOTNET/SebnWeb/Data/sebn.db'
out = r'C:/Users/asus/Desktop/app pfe/SEBN-DOTNET/inspect_db.json'
res = {'db_path': path, 'exists': os.path.exists(path)}
if res['exists']:
    conn = sqlite3.connect(path)
    cur = conn.cursor()
    cur.execute("SELECT name, type FROM sqlite_master WHERE type IN ('table','view') ORDER BY name")
    tables = [tuple(r) for r in cur.fetchall()]
    res['tables'] = tables
    res['foreign_keys_enabled'] = cur.execute('PRAGMA foreign_keys').fetchone()[0]
    res['table_info'] = {}
    for (name,) in cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"):
        info = {}
        info['columns'] = [tuple(c) for c in cur.execute(f"PRAGMA table_info('{name}')").fetchall()]
        info['foreign_keys'] = [tuple(f) for f in cur.execute(f"PRAGMA foreign_key_list('{name}')").fetchall()]
        info['count'] = cur.execute(f"SELECT COUNT(*) FROM '{name}'").fetchone()[0]
        info['sample'] = [list(r) for r in cur.execute(f"SELECT * FROM '{name}' LIMIT 5").fetchall()]
        res['table_info'][name] = info
    conn.close()
with open(out,'w',encoding='utf-8') as f:
    json.dump(res,f,ensure_ascii=False,indent=2)
print('WROTE', out)
