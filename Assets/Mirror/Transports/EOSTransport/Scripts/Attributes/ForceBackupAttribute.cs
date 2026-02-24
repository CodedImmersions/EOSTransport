using System;
using UnityEngine;

namespace EpicTransport.Attributes
{
    /// <summary>
    /// Indicates to EOSTransport's Host Migration System that this networked field should be backed up, even if it's not networked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ForceBackupAttribute : PropertyAttribute { }
}
