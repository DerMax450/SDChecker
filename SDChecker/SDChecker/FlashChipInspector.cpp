#include <iostream>
#include <Windows.h>
#include <SetupAPI.h>
#include <initguid.h>
#include <devguid.h>
#include <regstr.h>
#include <vector>
#include <winioctl.h>
#include <string>
#include <tchar.h>

#pragma comment(lib, "setupapi.lib")

void PrintDriveGeometry(HANDLE hDevice) {
    DISK_GEOMETRY pdg = { 0 };
    DWORD junk = 0;
    if (DeviceIoControl(hDevice, IOCTL_DISK_GET_DRIVE_GEOMETRY,
        NULL, 0, &pdg, sizeof(pdg), &junk, (LPOVERLAPPED)NULL)) {
        std::cout << "Cylinders: " << pdg.Cylinders.QuadPart << std::endl;
        std::cout << "Tracks per Cylinder: " << pdg.TracksPerCylinder << std::endl;
        std::cout << "Sectors per Track: " << pdg.SectorsPerTrack << std::endl;
        std::cout << "Bytes per Sector: " << pdg.BytesPerSector << std::endl;
        std::cout << "Disk Size (GB): " << (pdg.Cylinders.QuadPart * pdg.TracksPerCylinder * pdg.SectorsPerTrack * pdg.BytesPerSector) / (1024.0 * 1024.0 * 1024.0) << std::endl;
    } else {
        std::cerr << "Failed to get geometry" << std::endl;
    }
}

void ListVolumes() {
    DWORD drives = GetLogicalDrives();
    for (char letter = 'A'; letter <= 'Z'; ++letter) {
        if ((drives >> (letter - 'A')) & 1) {
            std::wstring root = std::wstring(1, letter) + L":\\";
            wchar_t volName[MAX_PATH] = { 0 }, fs[MAX_PATH] = { 0 };
            DWORD serial = 0, maxLen = 0, flags = 0;
            if (GetVolumeInformationW(root.c_str(), NULL, 0, &serial, &maxLen, &flags, fs, MAX_PATH)) {
                std::wcout << L"Drive " << letter << L": File System: " << fs << L" Serial: " << std::hex << serial << std::endl;
            }
        }
    }
}

void PrintDeviceDetails(HDEVINFO hDevInfo, SP_DEVINFO_DATA& devInfo, DWORD index) {
    TCHAR buffer[1024];
    DWORD size = 0;

    if (SetupDiGetDeviceRegistryProperty(hDevInfo, &devInfo, SPDRP_DEVICEDESC, NULL, (PBYTE)buffer, sizeof(buffer), &size)) {
        std::wcout << L"Device Name: " << buffer << std::endl;
    }
    if (SetupDiGetDeviceRegistryProperty(hDevInfo, &devInfo, SPDRP_HARDWAREID, NULL, (PBYTE)buffer, sizeof(buffer), &size)) {
        std::wcout << L"Hardware ID: " << buffer << std::endl;
    }
    if (SetupDiGetDeviceRegistryProperty(hDevInfo, &devInfo, SPDRP_MFG, NULL, (PBYTE)buffer, sizeof(buffer), &size)) {
        std::wcout << L"Manufacturer: " << buffer << std::endl;
    }

    TCHAR devPath[64];
    swprintf_s(devPath, L"\\\\.\\PhysicalDrive%u", index);
    HANDLE hDevice = CreateFileW(devPath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (hDevice != INVALID_HANDLE_VALUE) {
        std::wcout << L"Geometry for " << devPath << L":" << std::endl;
        PrintDriveGeometry(hDevice);
        CloseHandle(hDevice);
    } else {
        std::wcerr << L"Cannot open " << devPath << std::endl;
    }
}

int main() {
    HDEVINFO hDevInfo = SetupDiGetClassDevs(&GUID_DEVCLASS_DISKDRIVE, NULL, NULL, DIGCF_PRESENT);
    if (hDevInfo == INVALID_HANDLE_VALUE) {
        std::cerr << "SetupDiGetClassDevs failed" << std::endl;
        return 1;
    }

    SP_DEVINFO_DATA devInfo;
    devInfo.cbSize = sizeof(SP_DEVINFO_DATA);

    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &devInfo); ++i) {
        std::wcout << L"--- Device " << i << L" ---" << std::endl;
        PrintDeviceDetails(hDevInfo, devInfo, i);
        std::wcout << L"------------------------" << std::endl;
    }

    if (GetLastError() != ERROR_NO_MORE_ITEMS) {
        std::cerr << "Device enumeration failed" << std::endl;
    }

    ListVolumes();
    SetupDiDestroyDeviceInfoList(hDevInfo);
    return 0;
}
