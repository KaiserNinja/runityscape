﻿using Scripts.Game.Defined.Serialized.Characters;
using Scripts.Model.Buffs;
using Scripts.Model.Characters;
using Scripts.Model.Interfaces;
using Scripts.Model.Items;
using Scripts.Model.Processes;
using Scripts.Model.Spells;
using Scripts.Model.Stats;
using Scripts.Model.TextBoxes;
using Scripts.Presenter;
using Scripts.View.ObjectPool;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Scripts.Model.Pages {

    public class Battle : Page {
        private const string EXPERIENCE_GAIN = "{0} gains {1} {2}.";

        private const string BATTLE_START = "The battle begins.";

        private const string BUFF_AFFECT = "<color=yellow>{0}</color> is affected by <color=cyan>{1}</color>.";

        private const string BUFF_FADE = "<color=yellow>{0}</color>'s {1} fades.";

        private const int CAPACITY_FLAGGED_ITEM_LOOT_AMOUNT = 1;

        private const string CHARACTER_DEATH = "<color=yellow>{0}</color> has been <color=red>defeated</color>.";

        private const string DEFEAT = "Defeat!";

        private const string LOOT_MESSAGE = "{0}({1}) was added to the inventory.({2})";

        private const string PLAYER_QUESTION = "What will <color=yellow>{0}</color> do?";

        private const string ROUND_START = "Turn {0}";

        private const string VICTORY = "Victory!";

        private Page defeat;

        private HashSet<Character> leftGraveyard;

        private HashSet<Character> rightGraveyard;

        private int turnCount;

        private Page victory;

        private IDictionary<Item, int> loot;

        private bool wasExperienceGiven;

        public Battle(Page defeat, Page victory, Music music, string location, IEnumerable<Character> left, IEnumerable<Character> right) : base(location) {
            this.wasExperienceGiven = false;
            this.loot = new Dictionary<Item, int>();
            this.leftGraveyard = new HashSet<Character>(new IdentityEqualityComparer<Character>());
            this.rightGraveyard = new HashSet<Character>(new IdentityEqualityComparer<Character>());
            this.defeat = defeat;
            this.victory = victory;
            this.Music = music.GetDescription();
            turnCount = 0;
            foreach (Character c in left) {
                Left.Add(c);
            }
            foreach (Character c in right) {
                Right.Add(c);
            }
            OnEnter += () => {
                if (!IsResolved) {
                    Presenter.Main.Instance.StartCoroutine(startBattle());
                } else {
                    PostBattle(PlayerStatus);
                }
            };
        }

        private enum PlayerPartyStatus {
            ALIVE,
            DEAD,
            NOT_FOUND
        }
        private bool IsResolved {
            get {
                return Left.All(c => c.Stats.State == State.DEAD) || Right.All(c => c.Stats.State == State.DEAD);
            }
        }

        private PlayerPartyStatus PlayerStatus {
            get {
                // Left is player party
                if (Left.Any(c => c.HasFlag(Characters.Flag.PLAYER))) {
                    return Left.Any(c => c.Stats.State == State.ALIVE) ? PlayerPartyStatus.ALIVE : PlayerPartyStatus.DEAD;

                    // right is player party
                } else if (Right.Any(c => c.HasFlag(Characters.Flag.PLAYER))) {
                    return Right.Any(c => c.Stats.State == State.ALIVE) ? PlayerPartyStatus.ALIVE : PlayerPartyStatus.DEAD;

                    //Player party not found
                } else {
                    return PlayerPartyStatus.NOT_FOUND;
                }
            }
        }

        private ICollection<Character> AllDefeated {
            get {
                List<Character> defeated = new List<Character>();
                if (VictoriousSide == Side.LEFT) {
                    defeated.AddRange(Right);
                    defeated.AddRange(rightGraveyard);
                } else {
                    defeated.AddRange(Left);
                    defeated.AddRange(leftGraveyard);
                }
                return defeated;
            }
        }

        private ICollection<Character> VictoriousParty {
            get {
                if (VictoriousSide == Side.LEFT) {
                    return Left;
                } else {
                    return Right;
                }
            }
        }

        private Side VictoriousSide {
            get {
                if (Left.Any(c => c.Stats.State == State.ALIVE)) {
                    return Side.LEFT;
                } else {
                    return Side.RIGHT;
                }
            }
        }
        private static void AddEquipmentToLoot(IDictionary<Item, int> loot, Equipment equipment) {
            List<EquippableItem> equipmentItems = ((IEnumerable<EquippableItem>)equipment).ToList();
            foreach (EquippableItem item in equipmentItems) {
                AddItemToDictionary(loot, item, 1);
                equipment.RemoveEquip(new Inventory(), item.Type); // Dummy inventory to remove equipment
            }
        }

        private static void AddInventoryToLoot(IDictionary<Item, int> loot, Inventory inventory) {
            List<Item> inventoryItems = ((IEnumerable<Item>)inventory).ToList();
            foreach (Item item in inventoryItems) {
                AddItemToDictionary(loot, item, inventory.GetCount(item));
                inventory.Remove(item, inventory.GetCount(item));
            }
        }

        private static void AddItemsToLoot(IDictionary<Item, int> loot, IEnumerable<Character> defeatedSide) {
            foreach (Character c in defeatedSide) {
                if (c.HasFlag(Characters.Flag.DROPS_ITEMS)) {
                    AddInventoryToLoot(loot, c.Inventory);
                    AddEquipmentToLoot(loot, c.Equipment);
                }
            }
        }

        private static void AddItemToDictionary(IDictionary<Item, int> loot, Item item, int count) {
            if (!loot.ContainsKey(item)) {
                loot.Add(item, 0);
            }
            loot[item] += count;
        }

        private static Process GetItemStackProcess(Page current, Grid previousGrid, Grid lootGrid, IDictionary<Item, int> loot, Item item, Inventory victorInventory) {
            return new Process(
                    string.Format("{0}({1})", item.Name, loot[item]),
                    item.Icon,
                    string.Format("Loot this item.\n{0}\n{1}",
                    Util.ColorString(string.Format("Stack of {0}", loot[item]), Color.grey),
                    item.Description),
                    () => {
                        int lootAmount = GetOneClickLootAmount(loot, item);
                        loot[item] -= lootAmount;
                        if (loot[item] <= 0) {
                            loot.Remove(item);
                        }
                        victorInventory.Add(item, lootAmount);
                        current.AddText(string.Format(LOOT_MESSAGE, item.Name, lootAmount, victorInventory.Fraction));
                        lootGrid.Invoke();
                    },
                    () => loot.ContainsKey(item) && loot[item] > 0 && victorInventory.IsAddable(item, GetOneClickLootAmount(loot, item)
                ));
        }

        private static Grid GetLootGrid(Page current, Grid previous, IDictionary<Item, int> loot, ICollection<Character> victoriousSide) {
            Grid lootGrid = new Grid("Loot");
            lootGrid.Tooltip = "Pick up dropped items.";
            lootGrid.Icon = Util.GetSprite("swap-bag");
            lootGrid.Condition = () => (loot.Count > 0);
            lootGrid.OnEnter = () => {
                lootGrid.List.Clear();
                lootGrid.List.Add(PageUtil.GenerateBack(previous));
                foreach (Item item in loot.Keys) {
                    if (loot[item] > 0) {
                        lootGrid.List.Add(GetItemStackProcess(current, previous, lootGrid, loot, item, victoriousSide.FirstOrDefault().Inventory));
                    }
                }
            };
            return lootGrid;
        }

        private static int GetOneClickLootAmount(IDictionary<Item, int> loot, Item item) {
            int lootAmount = 0;
            if (item.HasFlag(Items.Flag.OCCUPIES_SPACE)) {
                lootAmount = CAPACITY_FLAGGED_ITEM_LOOT_AMOUNT;
            } else {
                lootAmount = loot[item];
            }
            return lootAmount;
        }

        private bool CharacterCanCast(SpellParams c) {
            return c.Stats.State == State.ALIVE;
        }

        private IEnumerator CheckForDeath() {
            foreach (Character c in GetAll()) {
                if (c.Stats.State == State.DEAD) {
                    IList<Buff> dispellableOnDeath = c.Buffs.Where(b => b.IsDispellable).ToList();
                    foreach (Buff removableBuff in dispellableOnDeath) {
                        c.Buffs.RemoveBuff(RemovalType.DISPEL, removableBuff);
                    }
                    yield return new WaitForSeconds(0.50f);
                    AddText(string.Format(CHARACTER_DEATH, c.Look.Name));
                    HashSet<Character> graveyard = null;
                    if (Left.Contains(c)) {
                        graveyard = leftGraveyard;
                    } else {
                        graveyard = rightGraveyard;
                    }
                    if (!c.HasFlag(Characters.Flag.PERSISTS_AFTER_DEFEAT)) {
                        graveyard.Add(c);
                        Left.Remove(c);
                        Right.Remove(c);
                    }
                }
            }
        }

        private IEnumerator DetermineCharacterActions(IList<Character> chars, IList<IPlayable> plays, HashSet<Character> playerActionSet) {
            ICollection<PooledBehaviour> declarations = new List<PooledBehaviour>();
            for (int i = 0; i < chars.Count; i++) {
                Character c = chars[i];

                if (c.Stats.State == State.ALIVE) {
                    yield return new WaitForSeconds(0.25f);
                    PooledBehaviour textbox = null;

                    // Only show when player controlled is asked to make a move
                    bool brainIsPlayer = (c.Brain is Player);

                    // Helps show user which character is doing stuff
                    if (brainIsPlayer) {
                        textbox = AddText(string.Format(PLAYER_QUESTION, c.Look.DisplayName));
                    }

                    // Wait until this is true until we ask the next character what action they want to perform
                    bool isActionTaken = false;

                    // Pass to the Brain a func that lets that lets them pass us what action they want to take
                    c.Brain.PageSetup(this,
                        (p) => {
                            if (!playerActionSet.Contains(c)) {
                                plays.Add(p);
                                playerActionSet.Add(c);
                                isActionTaken = true;
                            } else {
                                Util.Assert(false, string.Format("{0}'s brain adds more than one IPlayable objects in its DetermineAction().", c.Look.DisplayName));
                            }
                        });

                    // Let brain decide now
                    c.Brain.DetermineAction();

                    // Wait until they choose a move. (This so the player can wait as long as they want)
                    yield return new WaitWhile(() => !isActionTaken);
                    ClearActionGrid();

                    ObjectPoolManager.Instance.Return(textbox); // Remove "What will X do?" textbox, reduce clutter

                    if (brainIsPlayer) {
                        Spell spell = plays.Last().MySpell;
                        declarations.Add(AddText(spell.SpellDeclareText)); // "X will do Y" helper textbox
                    }

                }
            }

            // Remove all "X will do Y" texts
            foreach (PooledBehaviour pb in declarations) {
                ObjectPoolManager.Instance.Return(pb);
            }
        }

        private void EndRound() {
            turnCount++;
        }

        private IButtonable GetLootButton(Grid previous) {
            ICollection<Character> graveyardToLoot = null;
            ICollection<Character> sideToLoot = null;
            if (VictoriousSide == Side.RIGHT) {
                graveyardToLoot = leftGraveyard;
                sideToLoot = Left;
            } else {
                graveyardToLoot = rightGraveyard;
                sideToLoot = Right;
            }
            AddItemsToLoot(loot, graveyardToLoot);
            AddItemsToLoot(loot, sideToLoot);
            return GetLootGrid(this, previous, loot, VictoriousParty);
        }

        private IEnumerator GoThroughBuffs(IList<Character> chars) {
            //End of round buff interactions
            foreach (Character myC in chars) {
                Character c = myC;
                yield return new WaitForSeconds(0.25f);
                Characters.Buffs buffs = c.Buffs;
                IList<Buff> timedOut = new List<Buff>();
                foreach (Buff myB in buffs) {
                    yield return new WaitForSeconds(0.1f);
                    Buff b = myB;
                    AddText(string.Format(BUFF_AFFECT, c.Look.DisplayName, b.Name));
                    b.OnEndOfTurn(c.Stats);
                    if (b.IsTimedOut) {
                        timedOut.Add(b);
                    }
                }
                foreach (Buff myB in timedOut) {
                    yield return new WaitForSeconds(0.1f);
                    Buff b = myB;
                    AddText(string.Format(BUFF_FADE, c.Look.DisplayName, b.Name));
                    buffs.RemoveBuff(RemovalType.TIMED_OUT, b);
                }
            }
        }

        private void MakeTargetBuffsReactToSpell(Spell spell) {
            foreach (Buff b in spell.Target.Buffs) {
                if (b.IsReact(spell)) {
                    AddText(string.Format("{0}'s {1} activates {2}'s {3}!",
                        spell.Caster.Look.DisplayName,
                        spell.Book.Name,
                        spell.Target.Look.DisplayName,
                        b.Name
                        ));
                    b.React(spell, spell.Target.Stats);
                }
            }
        }

        private IEnumerator PerformActions(List<IPlayable> plays) {
            plays.Sort();
            for (int i = 0; i < plays.Count; i++) {
                yield return new WaitForSeconds(0.25f);
                IPlayable play = plays[i];

                // Dead characters cannot unleash spells
                if (CharacterCanCast(play.MySpell.Caster)) { // Death check
                    bool wasPlayable = play.IsPlayable;
                    MakeTargetBuffsReactToSpell(play.MySpell);
                    yield return play.Play();
                    AddText(play.Text);
                }
            }
        }
        private void PostBattle(PlayerPartyStatus status) {
            Main.Instance.Sound.StopAllSounds();
            Grid postBattle = new Grid("Main");
            postBattle.OnEnter = () => {
                postBattle.List.Clear();
                if (status == PlayerPartyStatus.ALIVE || status == PlayerPartyStatus.NOT_FOUND) {
                    if (status == PlayerPartyStatus.ALIVE) {
                        postBattle.List.Add(GetLootButton(postBattle));
                        if (!wasExperienceGiven) {
                            wasExperienceGiven = true;
                            GiveExperienceToVictors();
                        }
                    }
                    Character anyoneFromVictorParty = VictoriousParty.FirstOrDefault();
                    postBattle.List.Add(PageUtil.GenerateItems(this, postBattle, new SpellParams(anyoneFromVictorParty), PageUtil.GetOutOfBattlePlayableHandler(this)));
                    postBattle.List.Add(PageUtil.GenerateEquipmentGrid(postBattle, new SpellParams(anyoneFromVictorParty), PageUtil.GetOutOfBattlePlayableHandler(this)));
                    postBattle.List.Add(victory);
                } else {
                    postBattle.List.Add(defeat);
                }
                Actions = postBattle.List;
            };
            postBattle.Invoke();
        }

        private void GiveExperienceToVictors() {
            foreach (Character victor in VictoriousParty) {
                int totalExp = 0;
                foreach (Character defeated in AllDefeated) {
                    totalExp += CalculateExperience(victor.Stats, defeated.Stats);
                }
                victor.Stats.AddToStat(StatType.EXPERIENCE, Characters.Stats.Set.MOD_UNBOUND, totalExp);
                this.AddText(string.Format(EXPERIENCE_GAIN, Util.ColorString(victor.Look.DisplayName, Color.yellow), Util.ColorString(totalExp.ToString(), Color.yellow), StatType.EXPERIENCE.Name));
            }
        }

        /// <summary>
        /// Exp earned = Defeated.Level - Earner.Level
        /// </summary>
        /// <param name="earner">Whoever is earning the experience</param>
        /// <param name="defeated">Defeated character to do calculation for</param>
        /// <returns>Calculated experience earner should recieve</returns>
        private int CalculateExperience(Characters.Stats earner, Characters.Stats defeated) {
            return Mathf.Max(1, defeated.Level - earner.Level);
        }

        private IEnumerator startBattle() {
            AddText(BATTLE_START);
            while (!IsResolved) {
                yield return startRound();
                yield return new WaitForSeconds(1);
            }

            string message = string.Empty;
            PlayerPartyStatus result = PlayerStatus;
            if (result == PlayerPartyStatus.ALIVE) {
                message = VICTORY;

            } else if (result == PlayerPartyStatus.DEAD) {
                message = DEFEAT;

            } else { // Didn't find party
                message = "...";

            }

            AddText(message);
            PostBattle(result);
        }
        private IEnumerator startRound() {
            AddText(string.Format(Util.ColorString(ROUND_START, Color.grey), turnCount));
            List<IPlayable> plays = new List<IPlayable>();
            List<Character> chars = GetAll();
            HashSet<Character> playerActionSet = new HashSet<Character>(new IdNumberEqualityComparer<Character>());

            yield return DetermineCharacterActions(chars, plays, playerActionSet);
            yield return PerformActions(plays);
            yield return GoThroughBuffs(chars);
            yield return CheckForDeath();
            EndRound();
        }
    }
}