﻿using System;
using System.Collections.Generic;
using Scripts.Model.Interfaces;
using Scripts.Model.Pages;
using Scripts.Model.Processes;
using Scripts.Model.Spells;
using UnityEngine;

namespace Scripts.Model.Characters {
    public static class BrainList {
        public static readonly SpellList.Attack Attack = new SpellList.Attack();

        public class Player : Brain {
            private Battle battle;

            public override void DetermineAction(Action<IPlayable> addPlay) {
                Grid main = new Grid("Back");
                main.Array = Util.GetArray(
                    new Tuple(0, GenerateTargets(main, Attack, addPlay)),
                    new Tuple(1, GenerateSpellBooks(main, Owner.Spells, addPlay))
                    );
                battle.Actions = main.Array;
            }

            protected override void PageSetupHelper(Battle battle) {
                this.battle = battle;
            }

            private Grid GenerateBackableGrid(Grid previous, Sprite icon, int size, string name, string tooltip) {
                Grid grid = new Grid(name);
                grid.Icon = icon;
                grid.Tooltip = tooltip;
                IButtonable[] buttons = new IButtonable[size + 1];
                buttons[0] = GenerateBack(previous);
                grid.Array = buttons;
                return grid;
            }

            private Grid GenerateSpellBooks(Grid previous, Characters.Spells spells, Action<IPlayable> addFunc) {
                Grid grid = GenerateBackableGrid(previous, Util.GetSprite("spell-book"), spells.Set.Count, "Spell", "Cast a spell.");
                int index = 1;
                foreach (SpellBook mySb in spells.Set) {
                    SpellBook sb = mySb;
                    if (!sb.Equals(Attack)) {
                        grid.Array[index++] = GenerateSpellProcess(grid, sb, addFunc);
                    }
                }
                return grid;
            }

            private Process GenerateSpellProcess(Grid previous, SpellBook sb, Action<IPlayable> addFunc) {
                return new Process(CreateDetailedSpellName(Owner.Stats, sb), sb.Icon, sb.CreateDescription(new SpellParams(Owner)),
                    () => {
                        if (sb.CasterHasResources(Owner.Stats)) {
                            GenerateTargets(previous, sb, addFunc).Invoke();
                        }
                    });
            }

            private string CreateDetailedSpellName(Characters.Stats caster, SpellBook sb) {
                return Util.ColorString(sb.Name, sb.CasterHasResources(caster));
            }

            private Grid GenerateTargets(Grid previous, SpellBook sb, Action<IPlayable> addFunc) {
                ICollection<Character> targets = sb.TargetType.GetTargets(Owner, battle);
                Grid grid = GenerateBackableGrid(previous, sb.Icon, targets.Count, sb.Name, sb.CreateDescription(new SpellParams(Owner)));

                grid.Icon = sb.Icon;

                int index = 1;
                foreach (Character myTarget in targets) {
                    Character target = myTarget;
                    grid.Array[index++] = GenerateTargetProcess(target, sb, addFunc);

                }
                return grid;
            }

            private Process GenerateTargetProcess(Character target, SpellBook sb, Action<IPlayable> addFunc) {
                return new Process(CreateDetailedTargetName(target, sb),
                                    target.Look.Sprite,
                                    sb.CreateTargetDescription(Owner, target),
                                    () => {
                                        if (sb.IsCastable(new SpellParams(Owner), new SpellParams(target))) {
                                            addFunc(Spells.CreateSpell(sb, this.Owner, target));
                                        }
                                    });
            }

            private string CreateDetailedTargetName(Character target, SpellBook sb) {
                return Util.ColorString(target.Look.DisplayName, sb.IsCastable(new SpellParams(Owner), new SpellParams(target)));
            }

            private Process GenerateBack(Grid previous) {
                return new Process(
                    "Back",
                    "Go back to the previous menu.",
                    () => previous.Invoke()
                    );
            }
        }

        public class DebugAI : Brain {
            public override void DetermineAction(Action<IPlayable> addPlay) {
                addPlay(Spells.CreateSpell(Attack, Owner, foes.PickRandom()));
            }
        }
    }
}