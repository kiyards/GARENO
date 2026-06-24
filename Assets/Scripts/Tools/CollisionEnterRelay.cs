using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ICollisionAcceptor
{
    public void _OnCollisionEnter(Collision collision);
    public void _OnCollisionExit(Collision collision);
}
public class CollisionEnterRelay : MonoBehaviour
{
    [SerializeField]
    [RequireInterface(typeof(ICollisionAcceptor))]
    private Object _acceptor;
    public ICollisionAcceptor Acceptor => _acceptor as ICollisionAcceptor;
    public LayerMask layer;
    void OnCollisionEnter(Collision collision)
    {
        if (layer.IsGameObjectInMask(collision.gameObject))
            Acceptor._OnCollisionEnter(collision);
    }
    void OnCollisionExit(Collision collision)
    {
        if (layer.IsGameObjectInMask(collision.gameObject))
            Acceptor._OnCollisionExit(collision);
    }
}
