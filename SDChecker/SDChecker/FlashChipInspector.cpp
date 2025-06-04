#include <iostream>
#include <Windows.h>
#include <SetupAPI.h>
#include <initguid.h>
#include <devguid.h>
#include <regstr.h>
#include <vector>
#include <winioctl.h>

#pragma comment(lib, "setupapi.lib")

void PrintDriveGeometry(HANDLE hDevice) {
    DISK_GEOMETRY pdg = { 0 };
    DWORD junk = 0;
    if (DeviceIoControl(hDevice, IOCTL_DISK_GET_DRIVE_GEOMETRY,
        NULL, 0, &pdg, sizeof(pdg), &junk, (LPOVERLAPPED)NULL)) {
        std::cout << "Zylinder: " << pdg.Cylinders.QuadPart << std::endl;
        std::cout << "Spuren/Kopf: " << pdg.TracksPerCylinder << std::endl;
        std::cout << "Sektoren/Spur: " << pdg.SectorsPerTrack << std::endl;
        std::cout << "Bytes/Sektor: " << pdg.BytesPerSector << std::endl;
    }
    else {
        std::cerr << "Fehler beim Abrufen der Geometrie" << std::endl;
    }
}

void ListVolumesForDrive(const std::wstring& deviceName) {
    DWORD drives = GetLogicalDrives();
    for (char letter = 'A'; letter <= 'Z'; letter++) {
        if ((drives >> (letter - 'A')) & 1) {
            std::wstring rootPath = std::wstring(1, letter) + L":\\";
            wchar_t volumeName[MAX_PATH] = { 0 };
            if (GetVolumeNameForVolumeMountPointW(rootPath.c_str(), volumeName, MAX_PATH)) {
                std::wcout << L"Laufwerk " << letter << L": - Volume: " << volumeName;
                wchar_t fs[MAX_PATH] = { 0 };
                DWORD serial = 0, maxLen = 0, flags = 0;
                if (GetVolumeInformationW(rootPath.c_str(), NULL, 0, &serial, &maxLen, &flags, fs, MAX_PATH)) {
                    std::wcout << L" (FS: " << fs << L")";
                }
                std::wcout << std::endl;
            }
        }
    }
}

int main() {
    HDEVINFO hDevInfo;
    SP_DEVINFO_DATA DeviceInfoData;
    DWORD i;

    hDevInfo = SetupDiGetClassDevs(&GUID_DEVCLASS_DISKDRIVE, 0, 0, DIGCF_PRESENT);
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        std::cerr << "Fehler bei SetupDiGetClassDevs" << std::endl;
        return 1;
    }

    DeviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    for (i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &DeviceInfoData); i++) {
        TCHAR buffer[1024];
        DWORD buffersize = 0;

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_DEVICEDESC, 0, (PBYTE)buffer, sizeof(buffer), &buffersize)) {
            std::wcout << L"Gerätename: " << buffer << std::endl;
        }

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_HARDWAREID, 0, (PBYTE)buffer, sizeof(buffer), &buffersize)) {
            std::wcout << L"HardwareID: " << buffer << std::endl;
        }

        if (SetupDiGetDeviceRegistryProperty(hDevInfo, &DeviceInfoData,
            SPDRP_MFG, 0, (PBYTE)buffer, sizeof(buffer), &buffersize)) {
            std::wcout << L"Hersteller: " << buffer << std::endl;
        }

        // Versuche auf PHYSICALDRIVE zuzugreifen
        WCHAR devPath[64];
        swprintf_s(devPath, L"\\\\.\\PhysicalDrive%u", i);
        HANDLE hDevice = CreateFile(devPath, 0,
            FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
            OPEN_EXISTING, 0, NULL);

        if (hDevice != INVALID_HANDLE_VALUE) {
            std::wcout << L"Geometrie für " << devPath << L":" << std::endl;
            PrintDriveGeometry(hDevice);
            CloseHandle(hDevice);
        }
        else {
            std::wcerr << L"Kann " << devPath << L" nicht öffnen." << std::endl;
        }

        std::wcout << L"--- Zugeordnete Volumes ---" << std::endl;
        ListVolumesForDrive(devPath);

        std::wcout << L"-----------------------------" << std::endl;
    }

    if (GetLastError() != NO_ERROR && GetLastError() != ERROR_NO_MORE_ITEMS) {
        std::cerr << "Fehler beim Auflisten der Geräte" << std::endl;
    }

    SetupDiDestroyDeviceInfoList(hDevInfo);
    return 0;
}
