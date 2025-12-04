using System;
using UnityEngine;

namespace EpicTransport.Attributes
{
    /// <summary>
    /// Indicates to MirrorVR's Host Migration System that this networked field should not be backed up.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DoNotBackupAttribute : PropertyAttribute { }
}
