using System;
using UnityEngine;

public class MainMenuCameraBehaviour : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 20f;
    [SerializeField] private Transform target;
    [SerializeField] private Transform targetCam;
    
    public bool shouldRotate = true;
    
    private void LateUpdate()
    {
        if (shouldRotate && target != null && targetCam != null)
        {
            transform.RotateAround(target.position, Vector3.up, rotationSpeed * Time.deltaTime);
            transform.LookAt(target);
        }
    }
}
