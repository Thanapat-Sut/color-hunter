using UnityEngine;

public class EnemyBase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] protected float moveSpeed = 2f;
    [SerializeField] protected bool facingRight = true;

    [Header("Patrol")]
    [SerializeField] protected bool usePatrol = true;
    [SerializeField] protected Transform[] patrolPoints;
    [SerializeField] protected float waitTimeAtPoint = 0.5f;

    [Header("Edge Detection")]
    [SerializeField] protected bool checkForEdges = true;
    [SerializeField] protected Transform edgeCheck;
    [SerializeField] protected float edgeCheckDistance = 0.5f;
    [SerializeField] protected LayerMask groundLayer;

    [Header("Wall Detection")]
    [SerializeField] protected bool checkForWalls = true;
    [SerializeField] protected Transform wallCheck;
    [SerializeField] protected float wallCheckDistance = 0.2f;

    protected Rigidbody2D rb;
    protected SpriteRenderer spriteRenderer;
    protected Animator animator;

    protected int currentPatrolIndex;
    protected float waitTimer;
    protected bool isWaiting;

    protected virtual void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    protected virtual void Update()
    {
        if (usePatrol && patrolPoints.Length > 0)
        {
            Patrol();
        }
        else
        {
            SimpleMove();
        }

        UpdateAnimations();
    }

    protected virtual void SimpleMove()
    {
        if (CheckEdge() || CheckWall())
        {
            Flip();
        }

        Move();
    }

    protected virtual void Patrol()
    {
        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0)
            {
                isWaiting = false;
            }
            return;
        }

        Transform targetPoint = patrolPoints[currentPatrolIndex];
        Vector2 direction = (targetPoint.position - transform.position).normalized;

        if (Vector2.Distance(transform.position, targetPoint.position) < 0.1f)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            isWaiting = true;
            waitTimer = waitTimeAtPoint;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if ((direction.x > 0 && !facingRight) || (direction.x < 0 && facingRight))
        {
            Flip();
        }

        rb.linearVelocity = new Vector2(direction.x * moveSpeed, rb.linearVelocity.y);
    }

    protected virtual void Move()
    {
        float direction = facingRight ? 1f : -1f;
        rb.linearVelocity = new Vector2(direction * moveSpeed, rb.linearVelocity.y);
    }

    protected virtual bool CheckEdge()
    {
        if (!checkForEdges || edgeCheck == null) return false;

        RaycastHit2D hit = Physics2D.Raycast(edgeCheck.position, Vector2.down, edgeCheckDistance, groundLayer);
        return hit.collider == null;
    }

    protected virtual bool CheckWall()
    {
        if (!checkForWalls || wallCheck == null) return false;

        Vector2 direction = facingRight ? Vector2.right : Vector2.left;
        RaycastHit2D hit = Physics2D.Raycast(wallCheck.position, direction, wallCheckDistance, groundLayer);
        return hit.collider != null;
    }

    protected virtual void Flip()
    {
        facingRight = !facingRight;

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = !facingRight;
        }
    }

    protected virtual void UpdateAnimations()
    {
        if (animator == null) return;

        animator.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
    }

    public virtual void Die()
    {
        ScoreManager.Instance?.DefeatEnemy();

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        rb.linearVelocity = Vector2.zero;
        GetComponent<Collider2D>().enabled = false;
        enabled = false;

        Destroy(gameObject, 1f);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                // Player jumped on top
                if (contact.normal.y < -0.5f)
                {
                    Die();

                    Rigidbody2D playerRb = collision.gameObject.GetComponent<Rigidbody2D>();
                    if (playerRb != null)
                    {
                        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, 10f);
                    }
                    return;
                }
            }

            // Player hit from side
            PlayerController player = collision.gameObject.GetComponent<PlayerController>();
            if (player != null)
            {
                player.Die();
            }
        }
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (edgeCheck != null && checkForEdges)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(edgeCheck.position, edgeCheck.position + Vector3.down * edgeCheckDistance);
        }

        if (wallCheck != null && checkForWalls)
        {
            Gizmos.color = Color.blue;
            Vector3 direction = facingRight ? Vector3.right : Vector3.left;
            Gizmos.DrawLine(wallCheck.position, wallCheck.position + direction * wallCheckDistance);
        }
    }
}
