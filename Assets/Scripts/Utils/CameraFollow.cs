using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Header("Follow Settings")]
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private bool useHorizontalBounds;
    [SerializeField] private bool useVerticalBounds;

    [Header("Bounds")]
    [SerializeField] private float minX = -10f;
    [SerializeField] private float maxX = 10f;
    [SerializeField] private float minY = -5f;
    [SerializeField] private float maxY = 5f;

    [Header("Look Ahead")]
    [SerializeField] private bool useLookAhead;
    [SerializeField] private float lookAheadDistance = 2f;
    [SerializeField] private float lookAheadSpeed = 5f;

    private Vector3 currentLookAhead;
    private Vector3 targetLookAhead;

    private void Start()
    {
        if (target == null)
        {
            if (PlayerController.Instance != null)
            {
                target = PlayerController.Instance.transform;
            }
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        if (useLookAhead)
        {
            UpdateLookAhead();
            desiredPosition += currentLookAhead;
        }

        if (useHorizontalBounds)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minX, maxX);
        }

        if (useVerticalBounds)
        {
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, minY, maxY);
        }

        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;
    }

    private void UpdateLookAhead()
    {
        float horizontalInput = Input.GetAxisRaw("Horizontal");

        if (Mathf.Abs(horizontalInput) > 0.1f)
        {
            targetLookAhead = new Vector3(horizontalInput * lookAheadDistance, 0f, 0f);
        }
        else
        {
            targetLookAhead = Vector3.zero;
        }

        currentLookAhead = Vector3.Lerp(currentLookAhead, targetLookAhead, lookAheadSpeed * Time.deltaTime);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void SetBounds(float newMinX, float newMaxX, float newMinY, float newMaxY)
    {
        minX = newMinX;
        maxX = newMaxX;
        minY = newMinY;
        maxY = newMaxY;
    }

    public void SnapToTarget()
    {
        if (target == null) return;

        Vector3 snapPosition = target.position + offset;

        if (useHorizontalBounds)
        {
            snapPosition.x = Mathf.Clamp(snapPosition.x, minX, maxX);
        }

        if (useVerticalBounds)
        {
            snapPosition.y = Mathf.Clamp(snapPosition.y, minY, maxY);
        }

        transform.position = snapPosition;
    }

    private void OnDrawGizmosSelected()
    {
        if (!useHorizontalBounds && !useVerticalBounds) return;

        Gizmos.color = Color.yellow;

        float left = useHorizontalBounds ? minX : -1000f;
        float right = useHorizontalBounds ? maxX : 1000f;
        float bottom = useVerticalBounds ? minY : -1000f;
        float top = useVerticalBounds ? maxY : 1000f;

        Vector3 bottomLeft = new Vector3(left, bottom, 0f);
        Vector3 bottomRight = new Vector3(right, bottom, 0f);
        Vector3 topLeft = new Vector3(left, top, 0f);
        Vector3 topRight = new Vector3(right, top, 0f);

        Gizmos.DrawLine(bottomLeft, bottomRight);
        Gizmos.DrawLine(bottomRight, topRight);
        Gizmos.DrawLine(topRight, topLeft);
        Gizmos.DrawLine(topLeft, bottomLeft);
    }
}
