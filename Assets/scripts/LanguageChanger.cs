using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Collections.Generic;

public class LanguageChanger : MonoBehaviour
{
    
    private bool active = false;

    public void ChangeLanguage(int _localeID)
    {
        if (active == true)
        {
            return;
        }

        StartCoroutine(SetLocale(_localeID));
    }

    IEnumerator SetLocale(int _localeID)
    {
        active = true;
        yield return LocalizationSettings.InitializationOperation;

        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales[_localeID];
        active = false;
    }
}
