using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ITriggerAcceptor
{
    public void _OnTriggerEnter(Collider collision);
    public void _OnTriggerExit(Collider collision);
}
public class TriggerEnterRelay : MonoBehaviour
{
    [SerializeField]
    [RequireInterface(typeof(ITriggerAcceptor))]
    private Object _acceptor;
    public ITriggerAcceptor Acceptor => _acceptor as ITriggerAcceptor;
    public LayerMask layer;
    void OnTriggerEnter(Collider collision)
    {
        //if ((layer.value & (1 << collision.transform.gameObject.layer)) > 0)
        if (layer.IsGameObjectInMask(collision.gameObject))
            Acceptor._OnTriggerEnter(collision);
    }
    void OnTriggerExit(Collider collision)
    {
        //if ((layer.value & (1 << collision.transform.gameObject.layer)) > 0)
        if (layer.IsGameObjectInMask(collision.gameObject))
            Acceptor._OnTriggerExit(collision);
    }
}