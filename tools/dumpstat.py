import os, sys
path = sys.argv[1]
s = open(path, encoding='utf-8').read()
lines = [l for l in s.splitlines() if 'ControlType' in l]
print('total elements:', len(lines))
texts = []
for l in lines:
    if 'Text' in l:
        i = l.find("'")
        if i >= 0:
            j = l.find("'", i + 1)
            texts.append((l.split()[1], l[i + 1:j]))
print('text elements:', len(texts))
for ct, name in texts[:50]:
    print('  ', ct, name[:70].encode('unicode_escape').decode('ascii'))
