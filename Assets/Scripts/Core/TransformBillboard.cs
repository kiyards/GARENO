using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    public class TransformBillboard : MonoBehaviour
    {
        Camera cam;
        private void Awake()
        {
            cam = Camera.main;
        }
        private void LateUpdate()
        {
            transform.LookAt(transform.position + cam.transform.forward);
        }
    }
}