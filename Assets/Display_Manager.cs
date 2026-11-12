using UnityEngine;

public class Display_Manager : MonoBehaviour
{
    void Awake()
    {
        Debug.Log("Display count = " + Display.displays.Length);

        for (int i = 0; i < Display.displays.Length; i++)
        {
            Debug.Log($"Display {i}: {Display.displays[i].systemWidth}x{Display.displays[i].systemHeight}");

            if (i > 0)
            {
                Display.displays[i].Activate();
            }
        }
    }
}
