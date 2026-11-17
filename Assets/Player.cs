using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    public bool hasBallInHand = false;
    private GameController gc;

    public bool isBot = false;

    public GameObject markerPrefab;

    // Time the bot spends lining up before striking, so the turn reads clearly on screen.
    public float thinkTime = 0.8f;
    public float aimTime = 1.2f;

    private bool shotPending = false;

    void Start()
    {
        gc = GameObject.Find("GameController").GetComponent<GameController>();
        gc.onTurnEvent.AddListener(CheckRecieveTurn);
    }

    public void RecieveBallInHand()
    {
        hasBallInHand = true;
    }

    public void CheckRecieveTurn()
    {
        if (isBot && this == gc.activePlayer && !gc.isGameOver && !shotPending)
        {
            AutomaticShot();
        }
    }

    public void MakeBot()
    {
        isBot = true;
    }

    // True when no other ball lies on the swept path. Table cloth, rails and triggers are ignored:
    // the old version counted the cloth the balls rest on as an obstacle, so every shot was rejected.
    static bool PathClear(Vector3 from, Vector3 to, float radius, Transform ignoreA, Transform ignoreB)
    {
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 1e-4f) return true;
        RaycastHit[] hits = Physics.SphereCastAll(from, radius * 0.98f, dir / len, len, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            Ball other = h.collider.GetComponentInParent<Ball>();
            if (other == null) continue;
            if (other.transform == ignoreA || other.transform == ignoreB) continue;
            return false;
        }
        return true;
    }

    struct Shot { public Vector3 dir; public float power; public float score; }

    public void AutomaticShot()
    {
        if (gc.cueBall == null) return;

        Vector3 cuePos = gc.cueBall.transform.position;
        float radius = gc.cueBall.GetComponent<SphereCollider>().radius * gc.cueBall.transform.lossyScale.x;

        List<Ball> candidates = new();
        foreach (Ball b in gc.balls.GetAll())
        {
            if (b == null || b.CompareTag("CueBall")) continue;
            if (gc.rules.IsLegalBall(b.tag)) candidates.Add(b);
        }
        if (candidates.Count == 0)
        {
            foreach (Ball b in gc.balls.GetAll())
                if (b != null && !b.CompareTag("CueBall")) candidates.Add(b);
        }

        Shot best = new Shot { score = float.NegativeInfinity };
        bool found = false;

        if (gc.isBreakShot)
        {
            // Break: drive straight into the apex ball at full pace.
            Ball apex = Closest(candidates, cuePos);
            if (apex != null)
            {
                Vector3 d = apex.transform.position - cuePos; d.y = 0;
                best = new Shot { dir = d.normalized, power = 100f, score = 1 };
                found = true;
            }
        }
        else
        {
            GameObject[] pockets = GameObject.FindGameObjectsWithTag("Pocket");
            foreach (Ball b in candidates)
            {
                Vector3 bp = b.transform.position;
                foreach (GameObject pocket in pockets)
                {
                    Vector3 pp = new(pocket.transform.position.x, bp.y, pocket.transform.position.z);
                    Vector3 toPocket = pp - bp;
                    float potDist = toPocket.magnitude;
                    if (potDist < 1e-3f) continue;
                    Vector3 potDir = toPocket / potDist;

                    // Ghost-ball position: where the cue ball must be at impact.
                    Vector3 ghost = bp - potDir * (2f * radius);
                    Vector3 aim = ghost - cuePos; aim.y = 0;
                    float aimDist = aim.magnitude;
                    if (aimDist < radius) continue;

                    float cut = Vector3.Angle(aim, potDir);
                    if (cut > 75f) continue;

                    if (!PathClear(cuePos, ghost, radius, gc.cueBall.transform, b.transform)) continue;
                    if (!PathClear(bp, pp, radius, b.transform, null)) continue;

                    bool corner = pocket.name.StartsWith("Corner");
                    float cutFactor = Mathf.Cos(cut * Mathf.Deg2Rad);
                    float score = cutFactor * cutFactor * (corner ? 1f : 0.8f) / (1f + 0.15f * (aimDist + potDist));

                    if (score > best.score)
                    {
                        float power = Mathf.Clamp(18f + 9f * (aimDist + potDist / Mathf.Max(cutFactor, 0.35f)), 25f, 85f);
                        best = new Shot { dir = aim.normalized, power = power, score = score };
                        found = true;
                    }
                }
            }

            if (!found)
            {
                // No pot available: at least make legal contact with the nearest reachable legal ball.
                foreach (Ball b in candidates)
                {
                    if (!PathClear(cuePos, b.transform.position, radius, gc.cueBall.transform, b.transform)) continue;
                    Vector3 d = b.transform.position - cuePos; d.y = 0;
                    float s = -d.magnitude;
                    if (s > best.score) { best = new Shot { dir = d.normalized, power = 55f, score = s }; found = true; }
                }
            }
            if (!found)
            {
                // Last resort so the game never stalls.
                Ball b = Closest(candidates, cuePos);
                if (b != null)
                {
                    Vector3 d = b.transform.position - cuePos; d.y = 0;
                    best = new Shot { dir = d.normalized, power = 60f, score = 0 };
                    found = true;
                }
            }
        }

        Debug.Log($"[Bot] {name} found={found} power={best.power:F0}");
        if (found) StartCoroutine(AimAndShoot(best.dir, best.power));
    }

    static Ball Closest(List<Ball> list, Vector3 p)
    {
        Ball best = null; float bd = float.MaxValue;
        foreach (Ball b in list)
        {
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    IEnumerator AimAndShoot(Vector3 dir, float power)
    {
        shotPending = true;
        yield return new WaitForSeconds(thinkTime);

        Transform cue = gc.cue.transform;
        Quaternion from = cue.rotation;
        Quaternion to = Quaternion.LookRotation(dir, Vector3.up);
        float t = 0;
        while (t < aimTime)
        {
            t += Time.deltaTime;
            cue.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t / aimTime));
            yield return null;
        }
        cue.rotation = to;
        yield return new WaitForSeconds(0.35f);

        if (gc.activePlayer == this && !gc.isGameOver)
            gc.cue.TakeShot(power, Vector3.zero);
        shotPending = false;
    }
}
