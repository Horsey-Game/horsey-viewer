# horsey viewer

this is the repo for the horsey viewer. it is a map viewer for the horsey game that shows every horse on the map with its picture, lets you search and filter them, and gives you family trees and genome editing

it is a windows app made with c# winforms on .net 10

## what it does

- draws the map and every horse on it, with the horse's picture where the mapview mod saved one
- refresh asks the game for the horses. auto refresh does it every few seconds
- search, filter by wild / tame / hungry / tired / old / unrecorded, and minimum age
- read save loads every horse record from the save so you can open a family tree
- click family tree, then a horse on the map, to see its family
- double click a horse to edit its genome
- mouse wheel to zoom, drag to pan

## what you need

- horsey game
- the mapview mod, from the mod manager or the mod server. the viewer does nothing without it
- windows and .net 10 if you build it yourself

## building

```
dotnet build -c Release
```

or grab the zip from the releases page

## notes

- the viewer reads and writes the mapview mod's folder in the game's mods folder, and reads the map from the game's data folder. both paths are set at the top of Form1.cs
- hobby project so expect rough edges

## license

gpl-3.0. see LICENSE
