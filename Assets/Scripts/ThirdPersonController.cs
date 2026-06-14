using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float gravity = -9.81f;
    public float jumpHeight = 1.5f;

    [Header("Camera Settings")]
    public Transform cameraPivot;   // Empty GameObject as pivot (child of player, at shoulder height)
    public Transform playerCamera;  // Main Camera
    public float mouseSensitivity = 3f;
    public float cameraDistance = 5f;
    public float minY = -20f;
    public float maxY = 60f;

    [Header("Toggle Object")]
    public Transform attractObj;  // Assign the object you want to toggle
    public Transform repelObj;  // Assign the object you want to toggle
    public List<GameObject> toggleObject = new();  // Assign the object you want to toggle
    private Vector3 originalScale01, originalScale02;
    private bool isToggledOff = false;
    private bool isGrounded;
    public CharacterController controller;
    private Vector3 velocity;
    private float currentX = 0f;
    private float currentY = 0f;
    public Animator animator;
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        // Save the original scale
        if (attractObj != null && repelObj != null)
        {
            originalScale01 = attractObj.localScale;
            originalScale02 = repelObj.localScale;
            toggleObject.ForEach(x => x.SetActive(false));
        }
    }

    void Update()
    {
        HandleMovementAndJump();
        HandleToggle();
    }

    void LateUpdate()
    {
        HandleCameraOrbit();
    }

    void HandleMovementAndJump()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // --- Camera relative movement ---
        Vector3 camForward = playerCamera.forward;
        Vector3 camRight = playerCamera.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 move = camForward * moveZ + camRight * moveX;

        // --- Animation ---
        bool isWalking = move.magnitude > 0.1f;
        
        if(animator)animator.SetBool("Walk", isWalking);

        if (isWalking)
        {
            Quaternion targetRotation = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        // --- Gravity & Jump ---
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
            if(animator)animator.SetBool("Jump", false);
        }

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if(animator)animator.SetBool("Jump", true);
        }

        velocity.y += gravity * Time.deltaTime;

        // --- Final move (combine movement + gravity) ---
        Vector3 finalMove = move * moveSpeed * Time.deltaTime + velocity * Time.deltaTime;
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

    void HandleToggle()
    {
        if(toggleObject == null)return;
        if (attractObj == null && repelObj == null) return;
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (isToggledOff)
            {
                if(animator)animator.SetBool("Equip", false);
                attractObj.localScale = originalScale01;
                repelObj.localScale = Vector3.zero;
                toggleObject.ForEach(x => x.SetActive(false));
            }
            else
            {
                if(animator)animator.SetBool("Equip", true);
                attractObj.localScale = Vector3.zero;
                repelObj.localScale = originalScale02;
                toggleObject.ForEach(x => x.SetActive(true));
            }

            isToggledOff = !isToggledOff;
        }
    }
}
