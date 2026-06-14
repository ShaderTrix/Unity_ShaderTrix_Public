using UnityEngine;

public class SimpleFPSController : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float mouseSensitivity = 2f;

    public float jumpHeight = 1.2f;
    public float gravity = -9.81f;
    public float shotgunRange = 20f;
    public float shotgunRadius = 0.5f;
    public LayerMask hitMask;
    public ParticleSystem _hitParticle;
    Camera cam;
    CharacterController controller;

    float rotationX = 0f;
    Vector3 velocity;

    void Start()
    {
        cam = Camera.main;
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        Look();
        Move();
        Jump();
        ClickAction();
    }

    void Move()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move = transform.right * h + transform.forward * v;
        controller.Move(move * moveSpeed * Time.deltaTime);

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void Jump()
    {
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Keeps grounded
        }

        if (Input.GetButtonDown("Jump") && controller.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        cam.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void ClickAction()
    {
        if (Input.GetMouseButtonDown(0))
        {
            ShotgunCast();
        }
    }
    void ShotgunCast()
{
    Vector3 origin = cam.transform.position;
    Vector3 direction = cam.transform.forward;

    RaycastHit[] hits = Physics.SphereCastAll(
        origin,
        shotgunRadius,
        direction,
        shotgunRange,
        hitMask,
        QueryTriggerInteraction.Ignore
    );

    if (hits.Length == 0)
    {
        return;
    }

    foreach (var hit in hits)
    {
        if (hit.collider.TryGetComponent<PixelizerHideObject>(out var enemy))
        {
            enemy._hideObject = !enemy._hideObject;
            enemy.TakeDamage();
        }
        _hitParticle.Play();
    }
}
}
