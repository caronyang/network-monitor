import sys

data = open(r'D:\99_Temp\opencode\networkmonitor\src\NetworkMonitor\bin\Release\net10.0-windows10.0.19041\NetworkMonitor.dll', 'rb').read()
src = open(r'D:\99_Temp\opencode\networkmonitor\src\NetworkMonitor\UI\StatsForm.cs', encoding='utf-8').read()

probe = ' · '
print('probe codepoints:', [hex(ord(c)) for c in probe])
print('probe utf16 in dll:', probe.encode('utf-16-le') in data)
print('src contains probe:', probe in src)

# every non-ascii char in src
chars = sorted({c for c in src if ord(c) > 127})
print('non-ascii in src:', [(c, hex(ord(c))) for c in chars])

# find the Range line
for line in src.splitlines():
    if '_range.Text' in line:
        print('LINE:', line.strip().encode('unicode_escape').decode())
