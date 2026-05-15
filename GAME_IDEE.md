Authoritative Server
Bedeutet:

Der Server entscheidet ALLES Wichtige.

Server bestimmt:
Damage
Treffer
Enemy AI
XP
RNG
Cooldowns
Position Validation
Status Effects
Clients schicken nur:
Inputs
Movement
Skill Usage

Das ist Standard bei:

League of Legends
Dota 2
Valorant
Das wird dein größtes Technikproblem
Nicht die Spieler.

Sondern:

die Gegnerhorden + Skills.

Denn du willst vielleicht:

10 Spieler
2000 Enemies
5000 Projectiles
AoE-Spam
DoTs
Summons
Chain-Lightning
Knockbacks

Das ist heavy.
Deshalb brauchst du vermutlich:
ECS / Data-Oriented Design

Das ist EXTREM passend für dein Spiel.

Gute Engine-Optionen
Unity

Sehr sinnvoll.

Mit:
Netcode for GameObjects
Burst Compiler
GPU Instancing

Kann tausende Entities stemmen.

Das wäre wahrscheinlich die pragmatischste Wahl.

Was ich empfehlen würde
MVP Architektur
Client:
Unity 3D
Topdown
ECS für Gameplay
Server:
Dedicated Headless Server
authoritative
ECS Simulation
Netzwerk:
UDP
Snapshot/Delta Updates
Client Prediction
Interpolation

Besser:
Der Server sendet:
wichtige Events
Enemy States
Player States
Seeds
Boss Events

Clients simulieren viel lokal.

Beispiel
Server sagt:
„Poison Cloud spawned“
Position
Radius
Dauer
Seed
Client:
rendert Effekte lokal
simuliert Partikel selbst

Das spart massiv Bandbreite.
Tickrate

Du brauchst wahrscheinlich:

20–30 Tick Server
interpolated client rendering

Mehr wäre teuer bei Horde-Gameplay.

Der wichtige Unterschied
Riftstorm

sagt:

„Das Spiel eskaliert komplett.“
Für DEIN Gameplay würde ich sagen:
Riftstorm

passt besser.

Weil dein Core ja ist:

Horden
Builds
Chaos
PvPvE
massive Spell-Kombos
Survivor-Eskalation

Und genau das vermittelt „Storm“.
Gefühl:
Chaos
Eskalation
riesige Teamfights
Horden
Magie
PvP-Chaos

Das passt extrem gut zu deinem Gameplay:

viele Gegner
massive Builds
Spell-Synergien
Endgame-Clashes

Der Name klingt dynamisch und actionreich.

Das wäre mein Favorit für dein Konzept.

Ja — das ist tatsächlich eine ziemlich interessante Idee.
Das ist nicht einfach „Vampire Survivors Multiplayer“, sondern eher:

> „Battle-Royale / MOBA / Survivor Hybrid“

Und das hat einige sehr starke Design-Vorteile.

Du beschreibst im Grunde:

* Earlygame → PvE-Farming wie Vampire Survivors
* Midgame → Map Control / Positioning wie League of Legends
* Endgame → PvP-Teamfights / Survival

Das könnte funktionieren, weil die Spannungskurve sehr natürlich ist.

---

# Warum die Idee gut ist

## 1. PvE erzeugt Builds

In normalen MOBAs:

* farmst du Gold
* kaufst Items

Hier:

* überlebst du Horden
* levelst Skills
* evolvierst Builds

Das ist viel dynamischer und befriedigender.

---

# 2. Das Match eskaliert automatisch

Stell dir vor:

## Early

Spieler:

* vermeiden sich eher
* clearen Camps
* stacken Builds
* suchen Evolutions

---

## Midgame

Teams beginnen:

* Objectives zu contesten
* Bosse zu fighten
* gegnerische Spieler zu jagen

---

## Lategame

Die Karte wird kleiner.
Horden werden brutaler.
Dann treffen:

* komplett eskalierte Builds
* riesige AoE-Kombos
* 5v5 Chaos

aufeinander.

Das klingt ehrlich gesagt ziemlich stark.

---

# Das ist der wichtigste Unterschied

## Nicht:

„LoL mit mehr Mobs“

## Sondern:

„PvE erzeugt das PvP“

Das ist der interessante Teil.

Die Monsterwellen sind:

* Progression
* Ressourcenquelle
* Druckmittel
* Zonen-Kontrolle

---

# Warum das streambar wäre

Weil permanent etwas passiert:

* XP Rain
* Evolutionen
* Boss Spawns
* Clutches
* Escapes
* massive Teamfights

Und Zuschauer verstehen sofort:
„Dieser Build ist insane.“

---

# Das eigentliche Gold:

## Emergent Gameplay

Beispiele:

* Teams farmen absichtlich andere Zonen
* Spieler baiten Horden in Gegner
* Tank-Build blockiert Wege
* Summoner floodet Jungle
* AoE Builds kontrollieren Objectives
* Assassin versucht isolierte Farmer zu picken

Das erzeugt automatisch Stories.

---

# Die größte Gefahr

## „Visual Noise“

Das wird dein härtestes Problem.

Wenn 10 Spieler:

* Survivor-Builds haben
* tausende Projektile erzeugen
* Horden kämpfen

dann wird der Bildschirm extrem schnell unlesbar.

Das ist wahrscheinlich DAS zentrale Problem des gesamten Spiels.

---

# Du brauchst deshalb:

## 1. Sehr klare FX

Nicht:

* Bildschirm voller Partikel

Sondern:

* klare Shapes
* starke Farben
* gute Kontraste
* limitierte Spell-Dichte

---

## 2. Begrenzte Projektilanzahl

Sonst stirbt:

* Übersicht
* Performance
* PvP-Lesbarkeit

---

## 3. Build Identity

Spieler müssen sofort erkennen:

* „Oh shit der Frost-Tank kommt“
* „Das ist der Poison Build“
* „Der Typ hat Meteor Evolution“

---

# Was ich designtechnisch machen würde

## Kein komplett automatisches Gameplay

Das wäre wichtig.

Wenn PvP relevant ist:

* brauchst du aktive Skills
* Dodges
* Movement
* Skillshots
* Timing

Sonst wird PvP zu zufälligem Statistik-Chaos.

---

# Sehr gute Mischung wäre:

## Passive Auto-Attacks

wie VS

PLUS

## aktive Fähigkeiten

wie MOBA

Beispiel:

* Q = Dash
* E = Shield
* R = Ultimate
* Auto Weapons = Survivor Style

Das könnte richtig gut funktionieren.

---

# Eigentlich erinnert das etwas an:

* Battlerite
* Heroes of the Storm
* Vampire Survivors
* Battle Royale Struktur

aber mit deinem eigenen Twist.

---

# Mein Eindruck

Die Idee hat deutlich mehr Originalität als:

* weiterer LoL-Klon
* weiterer Survivor-Klon
* weiterer Hero-Shooter

Denn die Kombination:

* PvE Scaling
* emergente Builds
* taktisches PvP
* Map Pressure
* Horden als Gameplay-Element

ist ziemlich spannend.

Vor allem wenn Matches:

* 15–25 Minuten
* schnell eskalierend
* leicht verständlich
* aber buildtief

werden.
Ja — das könnte tatsächlich eine sehr starke Idee sein, weil du mehrere Genres kombinierst, die einzeln schon extrem gut funktionieren:

* Vampire Survivors → Massenspaß, Progression, Dopamin
* League of Legends / Dota 2 → Teamplay, Builds, Rollen
* Risk of Rain 2 → Coop + Scaling
* Diablo II → Loot + Character Fantasy

Und genau die Mischung daraus gibt es bisher kaum wirklich gut.

---

# Warum das Potenzial hat

## 1. Mobas haben ein Problem

Viele Leute lieben:

* Teamplay
* Builds
* Helden
* Combos

ABER hassen:

* toxische Communities
* Last-Hitting
* hohe Einstiegshürde
* 45-Minuten-Matches
* sweating

Ein VS-/Survivor-MOBA könnte:

* viel zugänglicher sein
* mehr Fun-first sein
* trotzdem Tiefe haben

---

# Das eigentliche Gold:

## „MOBA-Teamfight ohne Laning-Phase“

Das ist wahrscheinlich die beste Beschreibung.

Viele Spieler wollen eigentlich:

* Teamfights
* Builds
* Ultimates
* Synergien
* Bosskämpfe

aber NICHT:

* Farmen
* Wave Management
* 30 Minuten Macro

---

# 5–10 Spieler Coop?

Das wäre stark.

## Beispiele:

* 5 Spieler = klassischer Squad
* 10 Spieler = Raid-Chaos

Stell dir vor:

* hunderte Gegner
* jeder baut verrückte Synergien
* ein Spieler tankt
* einer bufft
* einer summoned
* einer ist Crit-Maschine
* massive Bosses
* Bildschirm komplett eskaliert

Das kann extrem satisfying sein.

---

# Was du NICHT machen solltest

## Kein reiner VS-Klon

Das wäre zu wenig.

Du brauchst:

* echte Klassenidentität
* aktive Skills
* Teamrollen
* Build-Entscheidungen
* Boss Mechanics
* Objectives

---

# Was richtig gut funktionieren könnte

## Rollen wie in einem MMO/MOBA

### Tank

zieht Horden zusammen.

### Support

Heals/Buffs/Shields.

### DPS

AoE-Maschine.

### Controller

Freeze/Stuns/DoTs.

### Summoner

Minions/Turrets.

---

# Das eigentliche Suchtpotenzial

## Synergien

Zum Beispiel:

* Frost Mage friert alles
* Lightning Spieler macht Chain Crits auf Frozen Targets
* Necromancer explodiert Leichen
* Tank grouped alles zusammen
* Healer bufft Attack Speed

Das ist das Zeug, wodurch Leute hunderte Stunden spielen.

---

# Was technisch wichtig wäre

## Du brauchst:

### EXTREM gute Performance

Denn du willst:

* 10 Spieler
* tausende Gegner
* hunderte Skills
* viele FX

Das schreit nach:

* ECS
* Data-Oriented Design
* GPU Instancing
* Object Pooling
* Dedicated Server

---

# Wichtigster Punkt:

## Lesbarkeit

Viele Survivor-Klone sterben daran:

* nur Bildschirmmüll
* keine Übersicht
* alles explodiert permanent

Du brauchst:

* klare Silhouetten
* gute Farben
* starke Audio-Feedbacks
* readable FX

Das ist extrem wichtig.

---

# Mein Eindruck:

Die Idee ist deutlich besser als ein weiterer Standard-MOBA-Klon.

Weil:

* geringere Einstiegshürde
* mehr sofortiger Spaß
* besser für Coop
* streamerfreundlich
* perfekt für Sessions
* einfacher zu erweitern mit Seasons/Content

---

# Was ich bauen würde

## Core Loop

### Runde:

* 20–30 Minuten
* Objectives
* Midgame Bosses
* Escalating Chaos

### Danach:

* Loot
* Meta Progression
* neue Klassen
* neue Builds
* neue Evolutions

---

# Sehr wichtiger Punkt:

## Build-Evolutionen wie Vampire Survivors

Beispiel:

* Fireball + Crit Ring → Meteor Build
* Poison + Summons → Pest Army
* Lightning + Cooldown → Thunderstorm
* Ice + Tank → Frozen Fortress

DAS ist das Dopamin.

---

# Wenn du es richtig machst:

Das könnte zwischen:

* Vampire Survivors
* League of Legends
* Risk of Rain 2
* Path of Exile

liegen.

Und das ist ehrlich gesagt ein ziemlich spannender Space.
# Riftstorm — Vision / Ziel des Spiels

![Image](https://images.openai.com/static-rsc-4/N1bUxEyWxWb0KkkTmUoZzgkJ7cz8Rl4GlIkpdp15TviGftScWxZ_dbkn39g_M04-6j3Ml_FuMz7U4WC6G52dtRzYnTlpRUnLhKpZIS1O8bZ5VDgo1KA8pWkdrWj5NmqITxf7S97sZI7hn-nYcnW4-KAlCk56nqk0AsKwS86Cjfo6rFWDVWBbpsnLQFiMSeiL?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/5AIdY44rCZZAFy6na_mNQnycyQli61Zxt7WgUSicKldbGO3nGCNS4tZc0ilpjBAW18FDTuY5Y8UpKxBCO5R1L2IEe2h1IM3AG9xKl3AS0nE222Z8zLAKdsHL5a9EoUx7hhw_EZfT3CW6dlkPpsTaGumt67zcJ1oCUhze3EAj3eCL3dgjh4DZ__J3zg7oNaYX?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/vTAsHe9ZBfVStgeyX6CYwgn9KQJT_c5Lqoq6kBpokiEEhh_qG37POjx34OqUvFSTbOceHIxyC3NOSG2fDwJ4EffM6FunGWQFe58mqEZsybMSn4QhOSVFWstfd4R1SElfcQB6tpXSK8MPKy4WG0FqRpXT1_sy4chGba451Vv2XELtLNsjXDAH-4__UkSpnhpW?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/JyzsQ7iV49A0T_z83vG63mvRleIuIgBgp3TU3b_DalRrH-oVJ9MLcPnMfc50gkPIFZwYiT5kjXB70eLgrvWYWrs6JfMxAESh_OJRh9cLZAm7TWByd3aKWIlXmjtoEnFnGkjaba1rGOceRY9yQW_C-n8k9TTuRG9wGP1nnj_DxgdXtd8d4fXFNldKOf9xBGUa?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/6_hG1Rx2tFd-P7icuMUDspYaFWquJLceFAcpHUkBYlFEgAqMcxdUNb3iddORwaKyS5LNX9Mqd0EMJvur-wdPvGtwXg9d6eMUdEnGAGF3CXVV9c_3BA3aXbbejYTtn9DUPKJAlbTsvwbp1qOWC82FsjXTsBbraqUOxYdXyw3nU7SejvJGmdDVqja6UMdHO698?purpose=fullsize)

![Image](https://images.openai.com/static-rsc-4/nV09O_OJo1pmoIzSf6zppCyGMkefUV95tIccqfYiQyoshMBuwBAUQK06in0AQ8xULbQ6HaRYjMYFCAnLQUISAZfk0UFwJzsFCsphcgI8tZ3oRXFFyP7j-ChXu-Ez3NoKt04DCLa8PrBFjgTW-vCLfLQzobX1VjILb3Yr96JHRtvjQ8J91w9gbNi8-dusDvPH?purpose=fullsize)

## Elevator Pitch

> Ein PvPvE Topdown-Actiongame, das:

* die Build-Eskalation von Vampire Survivors,
* die Teamfights von League of Legends,
* die Item-/Skill-Synergien von Path of Exile
* und die Chaos-Eskalation von Roguelites kombiniert.

Spieler kämpfen:

* gegen Horden,
* leveln Builds,
* kontrollieren Objectives,
* und treffen später in massiven PvP-Teamfights aufeinander.

Das PvE erzeugt das PvP.

---

# Core Gameplay Loop

## Early Game

### Fokus:

* Farmen
* Build-Aufbau
* Risiko vs Reward
* Horden überleben

Spieler:

* vermeiden direkte Kämpfe
* clearen Monster
* leveln Skills
* sammeln Evolutions
* contesten kleinere Objectives

---

## Midgame

### Fokus:

* Map Control
* Bosses
* Team Movement
* erste PvP-Fights

Teams:

* fighten um Objectives
* jagen isolierte Spieler
* sichern Buffs
* kontrollieren Zonen

---

## Endgame

### Fokus:

* massive Teamfights
* eskalierte Builds
* kleinere Map
* Horde-Chaos

Dann treffen:

* voll eskalierte Charaktere
* Synergie-Builds
* riesige Spell-Combos
* Bossmechaniken
* PvP

aufeinander.

---

# Genre-Mix

## Kombination aus:

* Vampire Survivors
* League of Legends
* Risk of Rain 2
* Battlerite
* Path of Exile

Aber:

## weniger Sweaty als klassische MOBAs

und:

## zugänglicher + mehr Action-first.

---

# Technische Zielarchitektur

## Engine

### Empfehlung:

Unity + URP + ECS/DOTS

Warum:

* viele Entities
* gute Multiplayer-Optionen
* schnelle Entwicklung
* performant
* perfekt für Topdown PvPvE

---

# Networking Architektur

## Dedicated Authoritative Server

### Server entscheidet:

* Damage
* Treffer
* Enemy AI
* XP
* RNG
* Cooldowns
* Position Validation
* Status Effects

### Clients schicken:

* Inputs
* Movement
* Skill Usage

---

# Warum das wichtig ist

Verhindert:

* Cheats
* Speedhack
* Fake Damage
* Desyncs
* Host Advantage

---

# Best Practices Multiplayer

## NICHT:

* jede Kugel synchronisieren
* jedes Partikel-Netzwerkobjekt senden
* jeden Effekt über Netzwerk schicken

Das skaliert nicht.

---

## STATTdessen:

### Server sendet:

* wichtige Events
* Seeds
* Enemy States
* Player States
* Boss Events

### Client simuliert lokal:

* FX
* Partikel
* kleine Bewegungen
* visuelle Darstellung

---

# Performance Best Practices

## EXTREM wichtig

Du willst:

* 10 Spieler
* hunderte bis tausende Gegner
* viele Skills
* Summons
* AoE
* Chain Lightning
* DoTs

Deshalb brauchst du:

---

## ECS / Data-Oriented Design

Nicht:

* tausende MonoBehaviours

Sondern:

* cachefreundliche Datenstrukturen
* Batch Processing
* Jobsysteme
* Burst Compiler

---

# Rendering Best Practices

## URP statt HDRP

Warum:

* bessere Performance
* bessere Skalierung
* bessere Multiplayer-Tauglichkeit
* ausreichend gute Grafik

---

## GPU Instancing

Sehr wichtig für:

* große Horden
* viele gleiche Gegner
* Performance

---

## Object Pooling

Niemals:

* ständig instantiate/destroy

Poolen:

* Gegner
* Projektile
* FX
* Damage Numbers

---

# Das größte Risiko des Spiels

# Visual Noise

Wenn:

* 10 Spieler
* tausende Gegner
* 5000 Projektile
* AoE Spam

gleichzeitig aktiv sind,
wird das Spiel schnell unlesbar.

---

# Deshalb brauchst du:

## Readability First

### Gute FX:

* klare Shapes
* starke Farben
* limitierte Effekte
* lesbare Silhouetten

---

## Wichtig:

Spieler müssen sofort erkennen:

* welcher Build aktiv ist
* welche Gefahr entsteht
* welcher Skill gecastet wird

---

# Gameplay Best Practices

## Kein Full Auto Gameplay

Das wäre schlecht für PvP.

---

# Beste Mischung:

## Passive Auto-Weapons

wie Vampire Survivors

PLUS

## aktive Skills

wie MOBAs

Zum Beispiel:

* Q = Dash
* W = Crowd Control
* E = Shield
* R = Ultimate

---

# Rollen-System

Sehr sinnvoll wären:

## Tank

zieht Horden zusammen.

## Support

bufft/heilt.

## DPS

AoE-Damage.

## Assassin

pickt isolierte Spieler.

## Summoner

kontrolliert Minions.

## Controller

Freeze/Stuns/Zone Control.

---

# Wichtigster Gameplay-Faktor

# Synergien

Beispiele:

* Frost + Lightning
* Poison + Summons
* Crit + Meteor
* Tank + Pull
* Corpse Explosion Builds

Das erzeugt:

* Dopamin
* Theorycrafting
* Meta
* Replayability

---

# Match Struktur

## Gute Matchlänge:

15–25 Minuten

Warum:

* genug Eskalation
* streamerfreundlich
* schnelle Queue-Zyklen
* weniger Frust

---

# Sehr wichtige Systeme

## Objectives

* Bosses
* Shrines
* Buff Camps
* Control Zones
* Evolutions

---

## Dynamic Map Events

Zum Beispiel:

* Blood Moon
* Elite Waves
* Rift Storm
* Map Collapse
* Monster Invasion

Das erzeugt:

* Bewegung
* Konflikte
* Chaos
* Spannung

---

# Langzeitmotivation

## Meta Progression

* neue Heroes
* neue Evolutions
* Cosmetics
* Talent Trees
* Ranked
* Seasons

---

# Was Riftstorm einzigartig macht

## Nicht:

„MOBA mit mehr Mobs“

## Sondern:

# „PvE erzeugt das PvP“

Die Horden sind:

* Druckmittel
* Ressourcenquelle
* Zone Control
* Skalierungssystem
* Chaosgenerator

Das ist die eigentliche Innovation deines Konzepts.

---

# Wahrscheinlich wichtigste Erfolgsfaktoren

## 1. Performance

Das Spiel MUSS butterweich laufen.

---

## 2. Readability

Spieler müssen verstehen, was passiert.

---

## 3. Satisfying Build Evolutions

Das ist dein Dopamin-System.

---

## 4. Gute Teamfights

Der eigentliche Höhepunkt jedes Matches.

---

## 5. Starke Art Direction

Stylized > realistischer Grafik-Overkill.

---

# Meine Empfehlung für dein MVP

## Start klein:

### Erst bauen:

* 2 Teams
* 1 Map
* 5 Heroes
* 10–15 Enemy Types
* 1 Boss
* einfache Evolutions
* 1 Matchmode

---

## NICHT sofort:

* MMO Features
* Open World
* 100 Heroes
* Crafting
* riesige Meta-Systeme

---

# Realistische erste technische Ziele

## MVP:

* 10 Spieler
* 300–500 Gegner gleichzeitig
* 60 FPS stabil
* simple AI
* wenige Netzwerkdaten
* klare FX

Wenn das funktioniert,
kannst du später eskalieren.
