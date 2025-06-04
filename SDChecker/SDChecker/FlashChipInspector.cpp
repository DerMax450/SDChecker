#include <iostream>
#include <Windows.h>
#include <SetupAPI.h>
#include <initguid.h>
#include <devguid.h>
#include <regstr.h>

#pragma comment(lib, "setupapi.lib")

int main() {
    HDEVINFO hDevInfo;
    SP_DEVINFO_DATA DeviceInfoData;
    DWORD i;

    hDevInfo = SetupDiGetClassDevs(&GUID_DEVCLASS_DISKDRIVE,
        0, 0, DIGCF_PRESENT);
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        std::cerr << "Fehler bei SetupDiGetClassDevs" << std::endl;
        return 1;
    }

    DeviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    for (i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &DeviceInfoData); i++) {
        TCHAR buffer[1024];
        DWORD buffersize = 0;

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_DEVICEDESC, 0, (PBYTE)buffer,
            sizeof(buffer), &buffersize)) {
            std::wcout << L"Gerätename: " << buffer << std::endl;
        }

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_HARDWAREID, 0, (PBYTE)buffer,
            sizeof(buffer), &buffersize)) {
            std::wcout << L"HardwareID: " << buffer << std::endl;
        }

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_MFG, 0, (PBYTE)buffer,
            sizeof(buffer), &buffersize)) {
            std::wcout << L"Hersteller: " << buffer << std::endl;
        }

        std::wcout << L"-----------------------------" << std::endl;
    }

    if (GetLastError() != NO_ERROR && GetLastError() != ERROR_NO_MORE_ITEMS) {
        std::cerr << "Fehler beim Auflisten der Geräte" << std::endl;
    }

    SetupDiDestroyDeviceInfoList(hDevInfo);
    return 0;
}
