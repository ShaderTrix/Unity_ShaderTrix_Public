using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ContextualAnimationSystem : MonoBehaviour
{
     [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float runSpeed = 8f;
    public float gravity => Physics.gravity.y;
    public float jumpHeight = 1.5f;

    [Header("Camera Settings")]
    [SerializeField]private Transform cameraPivot;   // Empty GameObject as pivot (child of player, at shoulder height)
    [SerializeField]private Transform playerCamera;  // Main Camera
    [SerializeField]private Animator animator;
    public float mouseSensitivity = 3f;
    public float cameraDistance = 5f;
    public float minY = -20f;
    public float maxY = 60f;

    private bool isGrounded;
    private CharacterController controller;
    private Vector3 velocity;
    private float currentX = 0f;
    private float currentY = 0f;
    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        HandleMovementAndJump();
    }

    void LateUpdate()
    {
        HandleCameraOrbit();
    }

    void HandleMovementAndJump()
{
    float moveX = Input.GetAxis("Horizontal");
    float moveZ = Input.GetAxis("Vertical");

    Vector3 camForward = playerCamera.forward;
    Vector3 camRight = playerCamera.right;
    camForward.y = 0f;
    camRight.y = 0f;
    camForward.Normalize();
    camRight.Normalize();

    Vector3 move = camForward * moveZ + camRight * moveX;

    bool hasMovement = move.magnitude > 0.1f;
    bool isRunning = hasMovement && Input.GetKey(KeyCode.LeftShift);

    float speed = isRunning ? runSpeed : moveSpeed;

    animator?.SetBool("Walk", hasMovement && !isRunning);
    animator?.SetBool("Run", isRunning);

    if (hasMovement)
    {
        Quaternion targetRotation = Quaternion.LookRotation(move);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * 10f
        );
    }

    isGrounded = controller.isGrounded;
    if (isGrounded && velocity.y < 0)
    {
        velocity.y = -2f;
        // animator?.SetBool("Jump", false);
    }

    if (Input.GetButtonDown("Jump") && isGrounded)
    {
        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        // animator?.SetBool("Jump", true);
    }

    velocity.y += gravity * Time.deltaTime;

    Vector3 finalMove =
        move.normalized * speed * Time.deltaTime +
        Vector3.up * velocity.y * Time.deltaTime;

    controller.Move(finalMove);
}


    void HandleCameraOrbit()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        currentX += mouseX;
        currentY -= mouseY;
        currentY = Mathf.Clamp(currentY, minY, maxY);

        cameraPivot.rotation = Quaternion.Euler(currentY, currentX, 0);

        Vector3 dir = new Vector3(0, 0, -cameraDistance);
        playerCamera.position = cameraPivot.position + cameraPivot.rotation * dir;
        playerCamera.LookAt(cameraPivot.position);
    }    
}
