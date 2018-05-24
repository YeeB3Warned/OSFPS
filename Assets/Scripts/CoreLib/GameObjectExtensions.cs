﻿using UnityEngine;

public static class GameObjectExtensions
{
    public static GameObject FindObjectOrAncestorWithTag(this GameObject gameObject, string tag)
    {
        if (gameObject.tag == tag) return gameObject;

        var parentTransform = gameObject.transform.parent;
        return parentTransform?.gameObject.FindObjectOrAncestorWithTag(tag);
    }
    public static ComponentType FindComponentInObjectOrAncestor<ComponentType>(this GameObject gameObject) where ComponentType : class
    {
        var component = gameObject.GetComponent<ComponentType>();
        if (component != null) return component;

        var parentTransform = gameObject.transform.parent;
        return parentTransform?.gameObject.FindComponentInObjectOrAncestor<ComponentType>();
    }

    public static GameObject FindDescendant(this GameObject gameObject, string descendantName)
    {
        var descendantTransform = FindDescendant(gameObject.transform, descendantName);
        return descendantTransform?.gameObject;
    }
    public static Transform FindDescendant(this Transform transform, string descendantName)
    {
        var descendantTransform = transform.Find(descendantName);
        if (descendantTransform != null) return descendantTransform;

        foreach (Transform childTransform in transform)
        {
            descendantTransform = FindDescendant(childTransform, descendantName);
            if (descendantTransform != null) return descendantTransform;
        }

        return null;
    }

    public static Vector3 GetHorizontalVelocity(Rigidbody rigidbody)
    {
        var velocity = rigidbody.velocity;
        return new Vector3(velocity.x, 0, velocity.z);
    }
}