﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using common.resources;
using wServer.logic;
using wServer.networking.packets.outgoing;
using wServer.realm.terrain;
using wServer.realm.worlds;

namespace wServer.realm.entities
{
    public class Enemy : Character
    {
        public bool isPet; // TODO quick hack for backwards compatibility
        bool stat;
        public Enemy ParentEntity;

        DamageCounter counter;
        public Enemy(RealmManager manager, ushort objType)
            : base(manager, objType)
        {
            stat = ObjectDesc.MaxHP == 0;
            counter = new DamageCounter(this);
        }

        public DamageCounter DamageCounter { get { return counter; } }

        public WmapTerrain Terrain { get; set; }

        Position? pos;
        public Position SpawnPoint { get { return pos ?? new Position() { X = X, Y = Y }; } }

        public override void Init(World owner)
        {
            base.Init(owner);
            if (ObjectDesc.StasisImmune)
                ApplyConditionEffect(new ConditionEffect()
                {
                    Effect = ConditionEffectIndex.StasisImmune,
                    DurationMS = -1
                });
        }

        public void SetDamageCounter(DamageCounter counter, Enemy enemy)
        {
            this.counter = counter;
            this.counter.UpdateEnemy(enemy);
        }

        public event EventHandler<BehaviorEventArgs> OnDeath;

        public void Death(RealmTime time)
        {
            counter.Death(time);
            if (CurrentState != null)
                CurrentState.OnDeath(new BehaviorEventArgs(this, time));
            OnDeath?.Invoke(this, new BehaviorEventArgs(this, time));
            Owner.LeaveWorld(this);
        }

        public int Damage(Player from, RealmTime time, int dmg, bool noDef, params ConditionEffect[] effs)
        {
            if (stat) return 0;
            if (HasConditionEffect(ConditionEffects.Invincible))
                return 0;
            if (!HasConditionEffect(ConditionEffects.Paused) &&
                !HasConditionEffect(ConditionEffects.Stasis))
            {
                var def = this.ObjectDesc.Defense;
                if (noDef)
                    def = 0;
                dmg = (int)StatsManager.GetDefenseDamage(this, dmg, def);
                int effDmg = dmg;
                if (effDmg > HP)
                    effDmg = HP;
                if (!HasConditionEffect(ConditionEffects.Invulnerable))
                    HP -= dmg;
                ApplyConditionEffect(effs);
                Owner.BroadcastPacketNearby(new Damage()
                {
                    TargetId = this.Id,
                    Effects = 0,
                    DamageAmount = (ushort)dmg,
                    Kill = HP < 0,
                    BulletId = 0,
                    ObjectId = from.Id
                }, this, null, PacketPriority.Low);

                counter.HitBy(from, time, null, dmg);
               
                if (HP < 0 && Owner != null)
                {
                    Death(time);
                }
                
                return effDmg;
            }
            return 0;
        }

        private int[] stealHits = { 0, 0 };

        public override bool HitByProjectile(Projectile projectile, RealmTime time)
        {
            if (stat) return false;
            if (HasConditionEffect(ConditionEffects.Invincible))
                return false;
            if (projectile.ProjectileOwner is Player &&
                !HasConditionEffect(ConditionEffects.Paused) &&
                !HasConditionEffect(ConditionEffects.Stasis))
            {
                var p = (projectile.ProjectileOwner as Player);

                var def = this.ObjectDesc.Defense;
                if (projectile.ProjDesc.ArmorPiercing)
                    def = 0;
                int dmg = (int)StatsManager.GetDefenseDamage(this, projectile.Damage, def);
                if (!HasConditionEffect(ConditionEffects.Invulnerable))
                    HP -= dmg;
                ConditionEffect[] effs = null;
                foreach (var pair in projectile.ProjDesc.CondChance) {
                    if (pair.Value == 0 || pair.Key == default(ConditionEffect)) continue;

                    if ((pair.Value / 100d) > (new Random().NextDouble())) {
                        var effList = new List<ConditionEffect>(projectile.ProjDesc.Effects);
                        effList.Add(pair.Key);
                        effs = effList.ToArray();
                    }
                }
                ApplyConditionEffect(effs ?? projectile.ProjDesc.Effects);
                Owner.BroadcastPacketNearby(new Damage()
                {
                    TargetId = this.Id,
                    Effects = projectile.ConditionEffects,
                    DamageAmount = (ushort)dmg,
                    Kill = HP < 0,
                    BulletId = projectile.ProjectileId,
                    ObjectId = projectile.ProjectileOwner.Self.Id
                }, this, (projectile.ProjectileOwner as Player), PacketPriority.Low);

                if (p?.stealAmount != null) {
                    if (p.stealAmount[0] != 0 && !p.HasConditionEffect(ConditionEffects.Sick)) {
                        int maxHP = p.Stats[0];
                        int lifeSteal = p.stealAmount[0];

                        if (lifeSteal >= 1 && p.HP < maxHP)
                            p.HP = ((p.HP + lifeSteal) > maxHP ? maxHP : p.HP + lifeSteal);
                        else {
                            stealHits[0]++;
                            if (stealHits[0] >= 1 / lifeSteal)
                                p.HP = ((p.HP + lifeSteal) > maxHP ? maxHP : p.HP + lifeSteal);
                        }
                    }
                    if (p.stealAmount[1] != 0 && !p.HasConditionEffect(ConditionEffects.Quiet)) {
                        int maxMP = p.Stats[1];
                        int manaLeech = p.stealAmount[1];

                        if (manaLeech >= 1 && p.MP < maxMP)
                            p.MP = ((p.MP + manaLeech) > maxMP ? maxMP : p.MP + manaLeech);
                        else {
                            stealHits[1]++;
                            if (stealHits[1] >= 1 / manaLeech)
                                p.MP = ((p.MP + manaLeech) > maxMP ? maxMP : p.MP + manaLeech);
                        }
                    }
                }

                counter.HitBy(projectile.ProjectileOwner as Player, time, projectile, dmg);

                if (HP < 0 && Owner != null)
                {
                    Death(time);
                }
                return true;
            }
            return false;
        }

        public override void Tick(RealmTime time)
        {
            if (pos == null)
                pos = new Position() { X = X, Y = Y };

            if (!stat && HasConditionEffect(ConditionEffects.Bleeding))
            {
                HP -= (int)(MaximumHP / 650f * time.ElapsedMsDelta / 1000f);
            }
            base.Tick(time);
        }
    }
}
