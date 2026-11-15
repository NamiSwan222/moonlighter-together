using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// Another player's Will, drawn from the sprites they reported. Has no collider and no "Player" tag:
    /// doors, pick-ups, triggers and enemy hitboxes on this copy never react to it.
    public sealed class Puppet : MonoBehaviour
    {
        public int PlayerId;
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>();
        private Vector2 _target;
        private bool _away;
        private TextMesh _label;
        private static Material _material;

        public void Init(int id)
        {
            PlayerId = id;
            var lgo = new GameObject("name");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = new Vector3(0, 46, 0);
            _label = lgo.AddComponent<TextMesh>();
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.characterSize = 2.2f;
            _label.fontSize = 40;
            _label.color = Color.white;
            var mr = lgo.GetComponent<MeshRenderer>();
            mr.sortingLayerName = "UI";
            mr.sortingOrder = 32000;
        }

        public void Snap(Vector2 p) { _target = p; transform.position = new Vector3(p.x, p.y, 0); }

        public void SetAway(bool away) { _away = away; }

        public void Apply(Presence.Remote r)
        {
            _target = r.Pos;
            if (((Vector2)transform.position - r.Pos).sqrMagnitude > 200f * 200f) Snap(r.Pos);   // a teleport / room change
            if (_material == null)
            {
                var hero = HeroMerchant.Instance;
                var sr = hero != null ? hero.GetComponentInChildren<SpriteRenderer>() : null;
                if (sr != null) _material = sr.sharedMaterial;
            }
            while (_parts.Count < r.Parts.Count)
            {
                var go = new GameObject("part" + _parts.Count);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                if (_material != null) sr.sharedMaterial = _material;
                _parts.Add(sr);
            }
            for (int i = 0; i < _parts.Count; i++)
            {
                var sr = _parts[i];
                if (i >= r.Parts.Count) { sr.enabled = false; continue; }
                var p = r.Parts[i];
                var sprite = SpriteCache.Find(p.Sprite);
                sr.enabled = sprite != null;
                if (sprite == null) continue;
                sr.sprite = sprite;
                sr.transform.localPosition = new Vector3(p.Offset.x, p.Offset.y, 0);
                sr.flipX = p.FlipX; sr.flipY = p.FlipY;
                sr.sortingLayerID = p.Layer; sr.sortingOrder = p.Order;
                Color c = p.Color;
                if (r.Ghost) c.a *= 0.45f;
                if (_away) c = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.6f);
                sr.color = c;
            }
            var player = Net.Session.Players.TryGetValue(PlayerId, out var pl) ? pl : null;
            _label.text = (player?.Name ?? ("P" + PlayerId)) + (_away ? " (reconnecting)" : r.Ghost ? " (down)" : "");
        }

        private void Update()
        {
            var p = Vector2.Lerp(transform.position, _target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            transform.position = new Vector3(p.x, p.y, 0);
        }
    }

    /// Sprites by name, from everything the game has loaded. Both copies run the same game, so a sprite one
    /// copy shows exists on the other once the same asset bundle is loaded; misses are looked up again later.
    public static class SpriteCache
    {
        private static readonly Dictionary<string, Sprite> ByName = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static float _nextRescan;
        public static int Misses;

        public static Sprite Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (ByName.TryGetValue(name, out var s) && s != null) return s;
            // A full rescan is costly: at most every 5 s, and not for names already known to be missing.
            if (Time.unscaledTime >= _nextRescan && (!Missing.Contains(name) || ByName.Count == 0))
            {
                _nextRescan = Time.unscaledTime + 5f;
                Rescan();
                if (ByName.TryGetValue(name, out s) && s != null) return s;
            }
            if (Missing.Count < 2000 && Missing.Add(name)) { Misses++; Plugin.Log.LogInfo("[puppet] no sprite named " + name + " loaded here (yet)"); }
            return null;
        }

        public static void Rescan()
        {
            ByName.Clear();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !ByName.ContainsKey(s.name)) ByName[s.name] = s;
        }
    }
}
