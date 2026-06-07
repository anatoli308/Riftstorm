
## erkante todos.

1. "Charge" muss die "swing"-Animation triggern und nicht die "cast"-Animation. Das ist ein Fehler, der behoben werden muss, damit die Animationen korrekt ablaufen. Außerdem wird man für 1 sec gestunned geht das?
2. nach dem "Charge" soll ein auto attack folgen, damit die Animationen flüssig und logisch aufeinander folgen. Derzeit wird nur gecharged, aber kein Auto-Attack ausgeführt, was die Animationen unvollständig macht.
3. Self-Buffs/Buffs/Debuffs müssen korrekt implementiert werden, damit sie die Charaktere stärken und die Gameplay-Mechanik verbessern. Derzeit fehlen diese Buffs, was das Spielerlebnis beeinträchtigt. 
So Buffs wie "Blessing of Champions", "Blessing of Defense", "Fortification Aura", "Divine Protection", "Wings of Freedom", "Vengance Aura"(increased damage), "Righteous Storm"(increased damage), "Shield Block"(increased Block rating by 14), "Aegis of Valor"(reduces all damage taken), "Magical Amplification"(increased spell/magical damage), "Vim of Wisdom"(increased intellect), "Boon of Clairvoyance"(increased spell crit rating by 30), "Boon of Protection"(Absorbs x damage and gain immunity to stuns), "Magical Dampening"(reduces spell/magical damage taken), "Warmth"(increased fire damage dealt by x% and reduces fire damage taken by x%), "Blessing of Health"(increased maximum health),
"Arrow Flurry"(Reduces the cooldown on Ranged Attack by x%), "Devotion"(Damage dealt by physical attacks is increased by $E1min% for 5 sec. Restores 20% mana when it expires.), "Focused Evasion"(Allows you to evade one hostile attack for $DUR.), "Mark Target"(Marks an enemy target, increasing all damage taken by 10% for $DUR.), "Inner Strength"(Increases a friendly target's damage by $E1min% for $DUR.), "Discipline"(Reduces all damage taken by a friendly target by $E1min% for 8 sec.), "Blessed Shield"(Absorbs $E1min damage. Lasts $DUR.), 

4. Die Buffs müssen auch visuell korrekt in der Character UI als Stats skaliert werden, damit die Spieler die Auswirkungen der Buffs klar erkennen können. Derzeit werden die Buffs nicht korrekt angezeigt, was zu Verwirrung führen kann.

5. Touch of Salvation soll jemand auf 100% health heilen. es passiert aber nichts.
6. Mighty Blow verursacht zusätzlich noch 12 Threat. Für Threat-Management brauche ich noch einen Wert pro Player wenn der Spieler angegriffen wird, damit ich den Threat entsprechend anpassen kann. Derzeit fehlt dieser Wert, was das Threat-Management erschwert. Derzeit ist einfach die aktuell angreifende Person derjenige mit dem meisten Threat, aber das ist nicht korrekt, da es auch andere Faktoren gibt, die den Threat beeinflussen können. Daher muss ein separater Wert für den Threat jedes Spielers implementiert werden, um eine genauere Berechnung zu ermöglichen.

7. Antimagic should interupt correctly and prevent the target from casting that school of magic for x seconds.

8. Chains of Ice slowt das target by -40% ist das korrekt am funktionieren ja oder ich denke schon.
9. Deep Freeze "encases the target in a block of ice" dafür bruache ich noch eine animation/sprite/particle?  um besser sichtbar zu sein. außerdem ist man währenddessen immun gegen alle angriffe das geht noch nicht korrekt.
10. Illusion Gate ist nicht korrekt ein sprite/animation/particle am spawnen für ein Illusion gate.





## später todo:
6. Retribution "A strike that becomes active after parrying or resisting an opponent's attack. This attack deals $E1min% weapon damage and pacifies the target, preventing it from using melee for $DUR.". die sollen dann gesilenced sein korrekt auch.
7. Redemption geht noch auf lebedingen zielen. Das ist sowas wie ressurect.

- kann ich die debuffs unter den buffs anzeigen lassen? Das wäre übersichtlicher, da die buffs dann oben und die debuffs unten angezeigt werden. Derzeit werden die debuffs nicht unter den buffs angezeigt, was die Übersichtlichkeit beeinträchtigt sondern einfach nebeneinander, was unübersichtlich sein kann. Daher wäre es besser, die debuffs unter den buffs anzuzeigen, um eine klarere Trennung zwischen positiven und negativen Effekten zu schaffen.

8. Ice Blast AOE particles/animation/sprites usw müssen besser aussehen! (stat_scale_x und effectx_radius) geht das korekt? Allgemein AOE ist noch zu prüfen.

9. Ice Shard hat ein Conditional es kann nur benutzt werden wenn chains of ice auf dem target drauf ist. das ist noch nicht korrekt implementiert.

10. Wisdom of Lazarus hat noch irgendwie instantly restores 0% mana.
11. Desparate Prayer instantly heals by 0% of their health and 0% of their mana. das ist noch nicht korrekt implementiert.
12. Vanish muss korrekt stealth implementieren. noch anschauen im original code wie vermutlich. loose threat etc... 
13. Satanic Madness ist eine art fear muss bis zu 5 targets um dich herum zum fliehen bringen. das ist noch nicht korrekt implementiert.
14. Tranquility Heals damage over time. das soll AOE heal sein.



## ordner struktur todos:

npc/ in enemies/ und npcs/ aufteilen.
powers/ und loot/ 

    /// <summary>
        /// Skill-Punkte pro 1 % Trefferchance-Bonus (Riftstorm-Erweiterung). Die
        /// NPC-Templates fuehren <c>melee_skill</c>/<c>ranged_skill</c> (5..125), die
        /// im Original-Server zwar geladen, aber nie in die Hit-Formel verrechnet
        /// wurden (<c>// TODO: Add hit rating</c>). Mit Faktor 25 ergibt sich ein
        /// moderater Bonus von 0..5 %, der exakt in den Headroom bis zum 100 %-Cap
        /// passt und vor allem den Level-Malus gegen hoehere Ziele abfedert.
        /// </summary>

        melee_skill und ranged_skill für unitstats einführen?!


        // FLARE-Konvention: 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW.
        private const int k_DefaultDirection = 2;
        ist diese konvention korrekt übernommen? oder hab ich da was vertauscht? also reihenfolge korrekt?