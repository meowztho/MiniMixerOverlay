#pragma once
// OWNED-PROCESS TEST ONLY: synthesizes host-published qualification for a
// fixture window. Not included in the actual native product DLL.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
static HANDLE OwnedPublishQualifiedWindow(HWND hwnd,const wchar_t* title){
    if(!hwnd||!title)return nullptr;
    const auto len=std::wcslen(title);
    if(!len||len>255)return nullptr;
    wchar_t name[100]{};
    swprintf_s(name,L"Local\\MiniMixerQualifiedTarget_%lu",GetCurrentProcessId());
    HANDLE map=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,
                                  0,560,name);
    if(!map||GetLastError()==ERROR_ALREADY_EXISTS){
        if(map)CloseHandle(map);
        return nullptr;
    }
    void* view=MapViewOfFile(map,FILE_MAP_WRITE,0,0,560);
    if(!view){CloseHandle(map);return nullptr;}
    std::uint8_t data[560]{};
    const std::uint32_t header[4]={0x4D4D5447,1,GetCurrentProcessId(),
                                     GetCurrentProcessId()+1};
    std::memcpy(data,header,sizeof(header));
    const std::uint64_t window=static_cast<std::uint64_t>(
                                    reinterpret_cast<UINT_PTR>(hwnd));
    std::memcpy(data+16,&window,sizeof(window));
    FILETIME started{},exit{},kernel{},user{};
    if(!GetProcessTimes(GetCurrentProcess(),&started,&exit,&kernel,&user)){
        UnmapViewOfFile(view);CloseHandle(map);return nullptr;
    }
    const std::uint64_t started64=(static_cast<std::uint64_t>(started.dwHighDateTime)<<32)
                                  |started.dwLowDateTime;
    std::memcpy(data+24,&started64,8);
    const auto size=static_cast<std::uint32_t>(len);
    std::memcpy(data+32,&size,4);
    std::memcpy(data+40,title,len*sizeof(wchar_t));
    std::memcpy(view,data,sizeof(data));
    UnmapViewOfFile(view);
    return map;
}
