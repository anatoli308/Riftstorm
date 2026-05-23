import json
from collections import Counter
d = json.load(open(r'd:\Riftstorm\Assets\StreamingAssets\items\_affixes.json', 'r', encoding='utf-8'))
items = list(d.values())
print('Total:', len(d))
print('Sample[0]:', json.dumps(items[0], indent=2))
print('noun=0 count:', sum(1 for x in items if x.get('name_single_noun') == 0))
print('noun=1 count:', sum(1 for x in items if x.get('name_single_noun') == 1))
print('other noun:', sum(1 for x in items if x.get('name_single_noun') not in (0, 1)))
print('stat_type1 top:', Counter(x['stat_type1'] for x in items).most_common(10))
print('multistats:', sum(1 for x in items if x.get('stat_type2', 0) > 0))
print('min/max level range:', min(x['min_level'] for x in items), '..', max(x['max_level'] for x in items))
print('--- prefix samples (noun=0) ---')
for x in [x for x in items if x.get('name_single_noun') == 0][:5]:
    print(' ', x['entry'], x['name'], 'stats:', [(x.get(f'stat_type{i}'), x.get(f'stat_value{i}')) for i in (1, 2, 3, 4) if x.get(f'stat_type{i}', 0) > 0], 'lvl', x['min_level'], '-', x['max_level'])
print('--- suffix samples (noun=1) ---')
for x in [x for x in items if x.get('name_single_noun') == 1][:5]:
    print(' ', x['entry'], x['name'], 'stats:', [(x.get(f'stat_type{i}'), x.get(f'stat_value{i}')) for i in (1, 2, 3, 4) if x.get(f'stat_type{i}', 0) > 0], 'lvl', x['min_level'], '-', x['max_level'])
