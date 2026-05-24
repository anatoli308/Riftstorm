## spells in _templates.jsos (1-10)

1. buffs spells die eigentlich auf friendly targets gecastet werden können. können nur mit ziel gecastet werden sollen aber ohne ein friendly target auf sich selbst gecastet werden können. (z.b. heal buff, der eigentlich nur auf friendly targets gecastet werden soll, aber auch ohne target auf sich selbst gecastet werden kann)
2. Buffs skalieren nicht korrekt viele haben 0% scaling  manche zeigen 4% oder so an aber es wird nicht korrekt übernommen bzw. für die stats skaliert in UI vermutlich auch nicht im gameplay? (Blessing of Champions, Blessing of Defense)
3. Der "Charge" skill soll nicht die "cast" animation abspielen, sondern die "swing" animation, damit es sich mehr wie ein Angriff anfühlt. (Charge currently plays the "cast" animation, but it should play the "swing" animation to feel more like an attack.). nach dem charge soll auch direkt auto attack starten falls möglich.
4. Fortification Aura soll für 15 sekunden "physical damage taken reduction" geben, also armor erhöhen das passiert aber nicht.
5. "Divine Strike" verursacht einen debuff der gestackt werden kann, das stacken geht und der debuff soll 10% mehr damage taken geben, das passiert aber nicht also wenn ich dann mit attacken treffe mehr schaden machen.
6. "Divine Protection" verursacht 75% damage reduction für 4 sekunden, das passiert aber nicht ich bekomme immernoch gleichen schaden von allen sources. (Fortification Aura vermutlich das gleiche Problem, da es auch damage reduction geben soll, aber das passiert auch nicht)
7. "Radiance" hat heal over time noch auf 0. "Heals a friendly targer for 4-4 and an addtional 0 over 6 seconds". das additional heal over time funktioniert nicht, es wird nur der initial heal gemacht aber kein heal over time.
8. "Holy Wrath" hat damage over time auf 0. "Deals 5-5 holy damage and an additional 3 Holy Damager over 3 seconds". das additional damage over time funktioniert nicht, es wird nur der initial damage gemacht aber kein damage over time.

-- 

Noch zu testen:

1. Wings of Freedom "immunity for crowd control effects" funktioniert nicht, ich bekomme immernoch cc effects wie stun oder slow obwohl ich den buff habe.
2. "Cleanse" vermutlich das gleiche Problem es soll poison oder diseases entfernen aber das passiert nicht, ich bekomme immernoch poison und diseases debuffs obwohl ich den skill benutze. 


## spells in _templates.jsos (11-20)


1. (11)"hammer of might" stunt das target für 2 sekunden und verursacht 0-0 holy damage. das damage ist als 0 angegeben? falsch skaliert?
2. Vengance Aura soll damage eines friendly target um 2% erhöhen aber es hat nicht funktioniert glaube ich ich habe gleichen damage gemacht siehe oben.
3. Also damage reduction oder damage increase buffs funktionieren generell nicht, ich bekomme immernoch den gleichen damage von allen sources obwohl ich buffs habe die eigentlich damage reduction oder damage increase geben sollten. (z.b. Fortification Aura, Divine Protection, Vengeance Aura, Salvation Aura, Righteous Storm, Shield Block, Aegis of Valor)

-- noch zu testen:

1. Touch of Salvation (hat derzeit 1007 mana cost) und nicht getestet ob klappt soll auf 100% heilen.
2. Mighty Blow soll 134% weapon damage machen (vermutlich geht das) und dann 7 threat generation geben (noch zu testen ob das funktioniert)
dafür auch in der UI das Portrait von target anpassen für threat generation damit man sieht wieviel threat man generiert hat.
3. Redemption soll jemand wiederbeleben noch nciht getestet ob das funktioniert, es soll auch mit 40% hp und 20% wiederbeleben, das soll auch getestet werden ob das funktioniert.
4. 

## spells in _templates.jsos (21-30)

1. "Magical Amplification" soll 2% mehr magical damage(spell damage) geben für friendly targets für 5min. siehe oben generell damage increase buffs funktionieren nicht, ich bekomme immernoch den gleichen damage von allen sources obwohl ich buffs habe die eigentlich damage increase geben sollten. (z.b. Fortification Aura, Divine Protection, Vengeance Aura, Salvation Aura, Righteous Storm, Shield Block, Aegis of Valor). Magical Dampening
2. "Vim of Wisdom". 
3. "Boon of Clairvoyance" soll spell crit um 30 erhöhen für 8sekunden. funktioniert vermutlich auch noch nicht siehe oben. Boon of Protection. 
4. Ignite damage over time siehe oben generell damage over time funktioniert nicht, ich bekomme nur den initial damage aber kein damage over time obwohl es in den tooltips steht das es damage over time geben soll. und auch zusatz noch 9% mehr fire damage taken(target) for 9sec (z.b. Ignite, Holy Wrath) 
5. Warmth increased fire damage dealt by 9% and fire damage taken by 9% for 9seconds. siehe oben.

-- noch zu testen:
1. "Antimagic" interupt a target spell casting. das soll eigentlich funktionieren aber ich habe es noch nicht getestet, es soll auch 4 sekunden nicht casten können, das soll auch getestet werden ob das funktioniert.


## spells in _templates.jsos (31-40)

1. Chains of Ice verursacht -40% movement speed ich glaube das klappt korrekt. prüfe das gerne.
2. Deep Freeze sollte für x sekunden target immun gegen alle damage sources machen eigentlich. und es kann für die zeit nicht angreifen. das nicht angreifen geht glaube ich aber immun gegen alle damage sources funktioniert nicht, ich bekomme immernoch schaden von allen sources obwohl ich den buff habe. vermutlich wegen siehe oben. 
3. "Illusion Gate" soll ein Return Obelisk an der position beschwören ich glaube die summoning pipeline funktioniert noch nicht. für prefabs/Textures halt oder so.
4. Wisdom of Lazarus gibt 1 mana every second for 3 das scheint zu funktionieren. aber es soll instantly x% mana restoren. das geht nicht das irgendwie noch 0% angezeigt wird bzw. vermutlich nicht korrekt skaliert. siehe oben vermutlich probleme.
5. 

-- noch zu testen:
1. "Remove Curse" prüfen ob es funktioniert, es soll curses entfernen.

## spells in _templates.jsos (41-50)

1. "Arrow Flurry" soll cooldown von reduced attacks by x% for x seconds verringern das funktioniert vermutlich noch nicht.
2. Devotion physical damage dealt increased by x% for x seconds. restores x% mana when it expires.
3. Focused Evasion Allows you to evade one hostile attack for x seconds. das funktioniert noch nicht. generell buff probleme
4. "multi-shot" fires three arrows at an enemy target in rapid succession. Das geht nicht das target bekommt garkein schaden.

## spells in _templates.jsos (51-60)

1. Sleep Arrow. das target soll für x sekunden schlafen und kann nicht angreifen oder sich bewegen. das funktioniert vermutlich noch nicht siehe oben generell buff probleme.
2. Vanish.


## spells in _templates.jsos (61-70)

1. Greater Heal hat irgendwie keine maana costs ??

## spells in _templates.jsos (71-80)

1. Plague ist ein DoT. diese gehen noch nicht.
2. Reincarnation soll target wiederbeleben mit 40% hp und 20% mana. das soll auch getestet werden ob das funktioniert. (soulstone wie bei warlock in wow 5minuten buff)
3. Resurrection soll target wiederbeleben mit 40% hp und 20% mana. das soll auch getestet werden ob das funktioniert.
4. Satanic madness soll up to five nearby enemies "flee in terror" fear.
5. Blessed Shield absorbs dmg.

-- noch zu testen:
1. Penance ist mana am drainen muss ich probieren.

##

- 81 : melee swing
- 82 : ranged attack
- 83 : Crit dummy
- 84: Loot sparkle
- 85: Loot
- 86: Quest ready
- 87: Quest done
- 88: Npc gossip
- 89: Minor life potion
- 90: Lesser life potion
- 91: Life potion
- 92: Minor Life serum
- 93: Lesser Life serum
- 94: Life serum
- 95: Minor Mana potion
- 96: Lesser Mana potion
- 97: Mana potion 
- 98: Minor Mana serum
- 99: Lesser Mana serum
- 100: Mana serum

- 101: Level up
- 102: Multi-Shot Arrow
- 103: Inspect Enemy
- 104: Greater Life Potion
- 105: Major Life Potion
- 106: Greater Life Serum
- 107: Major Life Serum
- 108: Greater Mana Potion
- 109: Major Mana Potion
- 110: Greater Mana Serum
- 111: Major Mana Serum

- 112: Rough Ruby
- 113: Rough Emerald
- 114: Rough Topaz
- 115: Rough Sapphire
- 116: Rough Amethyst
- 117: Ruby
- 118: Emerald
- 119: Topaz
- 120: Sapphire
- 121: Amethyst
- 122: Refined Ruby
- 123: Refined Emerald
- 124: Refined Topaz
- 125: Refined Sapphire
- 126: Refined Amethyst
- 127: Interact
- 128: Offer Duel
- 129: Minor Accessory Orb
- 130: Lesser Accessory Orb
- 131: Greater Accessory Orb
- 132: Major Accessory Orb
- 133: Minor Armor Orb
- 134: Lesser Armor Orb
- 135: Greater Armor Orb
- 136: Major Armor Orb
- 137: Minor Weapon Orb
- 138: Lesser Weapon Orb
- 139: Greater Weapon Orb
- 140: Major Weapon Orb

- 141: Throw Weapon

# npc spells/attacks/auras/dots/hots usw.
- 142: Insect Bite
- 143: Venomous Bite
- 144: Infected Wound
- 145: Cursed Poison

# learn spells
- 146: Learn: Wings of Freedom
- 147: Learn: Blessing of Champions
- 148: Learn: Blessing of Defense
- 149: Learn: Charge
- 150: Learn: Cleanse
- bis 225 : weitere learn spells für die restlichen skills

# npc spells/attacks/auras/dots/hots usw.
- 226: Poison Bolt
- 227: Lesser Poison Bolt
- 228: Howl of Terror
- 229: Rend
- 230: Mortal Wound
- bist 244: weitere npc spells/attacks/auras/dots/hots usw.

# interactions
- 245: Sleep

# scrolls spells
- 246: Scroll of Intellect I
- 247: Scroll of Intellect II
- 248: Scroll of Intellect III 
- 249: Scroll of Intellect IV
- 250: Scroll of Intellect V
- 251: Scroll of Willpower I
- 252: Scroll of Willpower II
- 253: Scroll of Willpower III
- 254: Scroll of Willpower IV
- 255: Scroll of Willpower V
- 256: Scroll of Courage I
- 257: Scroll of Courage II
- 258: Scroll of Courage III
- 259: Scroll of Courage IV
- 260: Scroll of Courage V
- 261: Scroll of Agility I
- 262: Scroll of Agility II
- 263: Scroll of Agility III
- 264: Scroll of Agility IV
- 265: Scroll of Agility V
- 266: Scroll of Strength I
- 267: Scroll of Strength II
- 268: Scroll of Strength III
- 269: Scroll of Strength IV
- 270: Scroll of Strength V

# consumables
- 271: Military Ration

# npc spells/attacks/auras/dots/hots usw.
- 272: Blight

- 273: Pick Lock
- 274: Sprint
- 275: War stomp
- 276: Shoot Bow

- 277: Drunk

# consumeables

- 278: Fresh Apple Pie
- 279: Gourmet Ham Sandwich
- 280: Oven Baked Turkey
- 281: Roasted Pork

# scrolls spells
- 282: Scroll of Returning


was ist hier passiert wo sind die paar hin?

- 300: Greater Fireball Volley
- 301: Bellowing Roar
- 302: Summon Guard
- 303: Power Word: Silence

- 304: Mind Blast +1
- 305: Shadow Bolt +1
- 306: Lesser Flame +1
- 307: Greater Flame +1
- 308: Frost bolt +1
- 309: Storm Bolt +1
- 310: Lesser Heal +1
- 311: Fire Bolt +1
- 312: Shaman Blast +1
- 313: Poison Bolt +1
- 314: Lesser Poison Bolt +1
- 315: Blight +1

- 316: Vortex
- 317: Apocalypse

- MISSING 318

- 319: duke's mind blast
- 320: duke's fury
- 321: divine storm bolt

- 322: Divine Accessory Orb
- 323: Divine Armor Orb
- 324: Divine Weapon Orb

- 325: Immobile
- 326: Shatter Gems
- 327: Create Undercourse Key
- 328: Extract Orb

- 329: Elixir of Indomitability

- 330: Frostbolt Volley
- 331: Firebolt Volley

- 332: Inevitable Shard

- 333: Greater Heal +1

- 334: Thunder Clap

- 335: Arcane Accessory Orb
- 336: Arcane Armor Orb
- 337: Arcane Weapon Orb
- 338: Greater Sprint

- 339: Flawless Ruby
- 340: Flawless Emerald
- 341: Flawless Topaz
- 342: Flawless Sapphire
- 343: Flawless Amethyst

- 344: Draconic Ruby
- 345: Draconic Emerald
- 346: Draconic Topaz
- 347: Draconic Sapphire
- 348: Draconic Amethyst

Missing dazwischen.

- 400 : Remove Magic
- 401: Netherverse
- 402: Finger of Death
- 403: Forcecage
- 404: Tranquile Volley
- 405: Tranquility
- 406: Disintegrate
- 409: Netherverse Pulse

- 410: Remove Magic



# allgemeine todos

1. healing floating text.
2. refactor interfec jsons
3. mouse click muss nur gedruckt gehalten werden zum laufen nicht mehrfach klicken.
4. wenn man während des laufens einen skill castet soll das laufen unterbrochen werden und man soll den skill casten können, aktuell 
läuft der character erst zum target versucht während des laufens zu casten macht es nicht und läuft zum ziel. er soll abrechen zu laufen sobald man einen skill castet und dann den skill casten können.
5. Der Flare_NPC Lone Howler hat irgendwie 120/110 HP ist das korrekt laut _template.json? ne oder.
6. Warum hat der Lone Howler so eine hohe auto attack range ? bzw. hohen aggro check?  ist es korrekt laut _template.json? ne oder. ich habe das selbst gebaut anstatt wie source oder hat source das überhaupt? ist vielleicht eine korrekte flare -> unity konvertierung notwendig ? 
7. channeling spells gibt es sowas überhaupt?
8. skill shots bzw. projectiles gibt es sowas überhaupt? z.b. fireball soll ja ein projectile sein, das von caster zum target fliegt, das soll auch in der animation sichtbar sein, aktuell ist es so das der damage direkt gemacht wird. es soll aber so sein das der damage erst gemacht wird wenn das projectile das target erreicht, also mit travel time. und während des travel time soll man das projectile auch sehen können, also die animation von fireball soll sichtbar sein und das projectile soll von caster zum target fliegen, wenn es das target erreicht soll der damage gemacht werden. das soll auch für andere skills gelten die projectiles haben sollen, z.b. multi-shot, shoot bow, etc.
9. Aoe spells (channeling, cast und aoe) gibt es sowas überhaupt? z.b. blizzard soll ein aoe spell sein, der eine area auf dem boden markiert und dann nach x sekunden die damage macht, während der markierung.
10. 