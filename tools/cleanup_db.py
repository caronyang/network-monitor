import sqlite3, sys, os

db = os.path.join(os.environ['LOCALAPPDATA'], 'NetworkMonitor', 'networkmonitor.db')
con = sqlite3.connect(db)
rows = con.execute('SELECT DISTINCT iface FROM traffic').fetchall()
low = [r[0] for r in rows if r[0] != r[0].upper()]
print('total distinct:', len(rows))
print('lowercase ifaces:', len(low))
for l in low[:10]:
    print('  ', l)
if low:
    q = 'DELETE FROM traffic WHERE ' + ' OR '.join(['iface=?'] * len(low))
    cur = con.execute(q, low)
    con.commit()
    print('deleted rows:', cur.rowcount)
left = con.execute('SELECT DISTINCT iface FROM traffic').fetchall()
print('remaining distinct:', len(left))
for r in left:
    print('  ', r[0])
con.close()
