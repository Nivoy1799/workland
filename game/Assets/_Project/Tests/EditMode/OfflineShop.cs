using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057: a ShopStage and an EconomyService without the network (EditMode): the server's own values (no synced field is written
    /// offline), one player (no connections: D40's solo crew), the shop wallet opened with <see cref="Make"/>'s balance.
    /// </summary>
    public sealed class OfflineShop : IDisposable
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        public ShopStage Stage { get; }
        public EconomyService Economy { get; }
        public int StageChanges { get; private set; }

        public static readonly EconomyCatalog EconomyData = EconomyCatalog.CreateDefault();
        public static ClimbCatalog Climb => FoodCornerRules.DefaultClimb;
        /// <summary>The solo crew's factors (no connection counts as one player).</summary>
        public static CrewFactors Solo => RestaurantRules.Crew(Climb, 1);

        private OfflineShop(long balance)
        {
            var economyGo = new GameObject("Offline economy");
            _made.Add(economyGo);
            Economy = economyGo.AddComponent<EconomyService>();
            Economy.StartGame(7UL, 1, balance);
            var stageGo = new GameObject("Offline stage");
            _made.Add(stageGo);
            Stage = stageGo.AddComponent<ShopStage>();
            typeof(ShopStage).GetField("_economy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Stage, Economy);
            Stage.StageChanged += () => StageChanges++;
        }

        public static OfflineShop Make(long balance) => new OfflineShop(balance);

        /// <summary>Stage 1 with the corner's pitch fee and the start unlock.</summary>
        public OfflineShop Corner(bool reached = true)
        {
            Stage.ServerBegin(ShopStageRules.FoodCorner, 500, reached, false, new[] { FoodCornerRules.StartUnlock, "future_unlock" }, 0, null, null);
            return this;
        }

        /// <summary>Stage 2 with <paramref name="unlocks"/> (the rent and the fries by default), rent owed and a star window.</summary>
        public OfflineShop Restaurant(IReadOnlyList<string> unlocks = null, long owed = 0, IReadOnlyList<int> window = null,
            IReadOnlyList<string> menuOff = null, bool reached = false)
        {
            Stage.ServerBegin(ShopStageRules.FirstRestaurant, 500, reached, false,
                unlocks ?? new[] { FoodCornerRules.StartUnlock, FoodCornerRules.GoalPurchase }, owed, window, menuOff);
            return this;
        }

        public void Dispose()
        {
            foreach (GameObject go in _made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
